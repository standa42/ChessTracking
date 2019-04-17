﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Kinect_v0._1;
using Microsoft.Kinect;
using Random = System.Random;
using Accord.Math;
using Accord.Math.Geometry;
using ChessTracking.MultithreadingMessages;
using MathNet.Spatial.Euclidean;
using Message = ChessTracking.MultithreadingMessages.Message;
using Point = Accord.Point;
using Point2D = MathNet.Spatial.Euclidean.Point2D;

namespace ChessTracking.ProcessingPipeline
{
    class AttemptToRecreateOldVisualiser
    {
        private State state = State.BeforeLocalization;
        private Tuple<double, double> anglesOfFirstRotation = null;
        MyVector3DStruct startingPointFinal = new MyVector3DStruct();
        MyVector3DStruct firstVectorFinal = new MyVector3DStruct();
        MyVector3DStruct secondVectorFinal = new MyVector3DStruct();
        bool[] resultBools = new bool[512 * 424];

        public BlockingCollection<Message> ProcessingOutputQueue { get; }
        private Bitmap bmpToSend;

        public AttemptToRecreateOldVisualiser(BlockingCollection<Message> processingOutputQueue)
        {
            ProcessingOutputQueue = processingOutputQueue;
        }

        public void Recalibrate()
        {
            Thread.Sleep(500);
            state = State.BeforeLocalization;
            anglesOfFirstRotation = null;
            startingPointFinal = new MyVector3DStruct();
            firstVectorFinal = new MyVector3DStruct();
            secondVectorFinal = new MyVector3DStruct();
            resultBools = new bool[512 * 424];
        }

        public void Update(byte[] colorFrameData, ushort[] depthData, ushort[] infraredData, CameraSpacePoint[] cameraSpacePointsFromDepthData,
            DepthSpacePoint[] pointsFromColorToDepth, ColorSpacePoint[] pointsFromDepthToColor)
        {
            if (state == State.Localization)
            {
                return;
            }

            if (state == State.BeforeLocalization)
            {
                state = State.Localization;
            }

            var canniedBytes = CannyAppliedToDepthData(cameraSpacePointsFromDepthData);
            Data data = new Data(cameraSpacePointsFromDepthData);

            data.CutOffMinMaxDepth(Config.minDepth, Config.maxDepth);
            try
            {
                if (state == State.Localization)
                {
                    data.RANSAC();
                    data.LargestTableArea();
                    data.LinearRegression();
                    data.LargestTableArea();
                    data.LinearRegression();
                    data.LargestTableArea();
                    data.LinearRegression();
                    data.LargestTableArea();
                }


                if (anglesOfFirstRotation == null)
                {
                    data.RotationTo2DModified();
                }
                else
                {
                    data.RotationTo2DModified(anglesOfFirstRotation.Item1, anglesOfFirstRotation.Item2);
                }

                if (state == State.Localization)
                {
                    resultBools = data.ConvexHullAlgorithmModified();
                }
            }
            catch (Exception) { }

            var colorImg = ReturnColorImageOfTable(resultBools, colorFrameData, pointsFromColorToDepth);
            //colorImg._EqualizeHist();;
            colorImg.Bitmap.Save(@"D:\Desktop\clr.jpeg", ImageFormat.Jpeg);

            Task.Run(() =>
            {
                Image<Bgr, Byte> drawnEdges2 = null;

                if (state == State.Localization)
                {

                    Image<Gray, Byte> grayImage = colorImg.Convert<Gray, Byte>();
                    var binarizedImg = new Image<Gray, byte>(grayImage.Width, grayImage.Height);
                    CvInvoke.Threshold(grayImage, binarizedImg, 200, 255, ThresholdType.Otsu);
                    Image<Gray, Byte> cannyEdges = binarizedImg.Canny(700, 1400, 5, true).SmoothGaussian(3)
                        .ThresholdBinary(new Gray(50), new Gray(255));
                    var lines = cannyEdges.HoughLinesBinary(
                        0.8f, //Distance resolution in pixel-related units
                        Math.PI / 1500, //Angle resolution measured in radians.
                        220, //threshold
                        100, //min Line width (90)
                        35 //gap between lines
                    )[0];
                    Image<Bgr, Byte> drawnEdges =
                        new Image<Bgr, Byte>(new Size(cannyEdges.Width, cannyEdges.Height) /*cannyEdges.ToBitmap()*/);
                    foreach (LineSegment2D line in lines)
                        CvInvoke.Line(drawnEdges, line.P1, line.P2,
                            new Bgr( /*Color.Red*/ /*RandomColor()*/ Color.White).MCvScalar, 1);

                    var lines2 = drawnEdges.Convert<Gray, byte>().HoughLinesBinary(
                        0.8f, //Distance resolution in pixel-related units
                        Math.PI / 1500, //Angle resolution measured in radians.
                        50, //threshold
                        100, //90                //min Line width
                        10 //gap between lines
                    )[0];

                    var linesTuple = FilterLinesBasedOnAngle(lines2, 25);

                    drawnEdges2 = new Image<Bgr, Byte>( /*colorImg.Bitmap*/
                        new Size(cannyEdges.Width, cannyEdges.Height));

                    var points = new List<Point2D>();
                    var contractedPoints = new List<Point2D>();
                    var libraryLines = new Tuple<List<Line2D>, List<Line2D>>(new List<Line2D>(), new List<Line2D>());

                    foreach (var lineSegment2D in linesTuple.Item1)
                    {
                        libraryLines.Item1.Add(new Line2D(new Point2D(lineSegment2D.P1.X, lineSegment2D.P1.Y),
                            new Point2D(lineSegment2D.P2.X, lineSegment2D.P2.Y)));
                    }

                    foreach (var lineSegment2D in linesTuple.Item2)
                    {
                        libraryLines.Item2.Add(new Line2D(new Point2D(lineSegment2D.P1.X, lineSegment2D.P1.Y),
                            new Point2D(lineSegment2D.P2.X, lineSegment2D.P2.Y)));
                    }

                    foreach (var line1 in libraryLines.Item1)
                    {
                        foreach (var line2 in libraryLines.Item2)
                        {
                            var accordLine1 =
                                new LineSegment(new Point((float)line1.StartPoint.X, (float)line1.StartPoint.Y),
                                    new Point((float)line1.EndPoint.X, (float)line1.EndPoint.Y));
                            var accordLine2 =
                                new LineSegment(new Point((float)line2.StartPoint.X, (float)line2.StartPoint.Y),
                                    new Point((float)line2.EndPoint.X, (float)line2.EndPoint.Y));

                            var accordNullablePoint = accordLine1.GetIntersectionWith(accordLine2);
                            if (accordNullablePoint != null)
                            {
                                points.Add(new Point2D(accordNullablePoint.Value.X, accordNullablePoint.Value.Y));
                            }
                        }
                    }

                    foreach (var line1 in linesTuple.Item1)
                    {
                        points.Add(new Point2D(line1.P1.X, line1.P1.Y));
                        points.Add(new Point2D(line1.P2.X, line1.P2.Y));
                    }

                    foreach (var line2 in linesTuple.Item2)
                    {
                        points.Add(new Point2D(line2.P1.X, line2.P1.Y));
                        points.Add(new Point2D(line2.P2.X, line2.P2.Y));
                    }


                    double distance = 12; //15
                    while (true)
                    {
                        // new list for points to average and add first element from remaining list
                        var pointsToAvg = new List<Point2D>();
                        var referencePoint = points.First();
                        pointsToAvg.Add(referencePoint);
                        points.RemoveAt(0);

                        // loop throught remaining list and find close neighbors
                        foreach (var point in points)
                        {
                            double diffX = (referencePoint.X - point.X);
                            double diffY = (referencePoint.Y - point.Y);

                            if (Math.Sqrt(diffX * diffX + diffY * diffY) < distance)
                            {
                                pointsToAvg.Add(point);
                            }
                        }

                        // remove them all from remaining list
                        foreach (var pointToRemove in pointsToAvg)
                        {
                            points.Remove(pointToRemove);
                        }

                        // compute average and add it to list
                        double x = 0;
                        double y = 0;
                        int count = 0;
                        foreach (var point in pointsToAvg)
                        {
                            x += point.X;
                            y += point.Y;
                            count++;
                        }

                        contractedPoints.Add(new Point2D((int)x / count, (int)y / count));

                        // if rem. list is empty -> break
                        if (points.Count == 0)
                        {
                            break;
                        }
                    }


                    foreach (var contractedPoint in contractedPoints)
                    {
                        try
                        {
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X, (int)contractedPoint.Y, Color.Red);
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X + 1, (int)contractedPoint.Y,
                                Color.Red);
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X, (int)contractedPoint.Y + 1,
                                Color.Red);
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X + 1, (int)contractedPoint.Y + 1,
                                Color.Red);
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X + 2, (int)contractedPoint.Y,
                                Color.Red);
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X, (int)contractedPoint.Y + 2,
                                Color.Red);
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X + 2, (int)contractedPoint.Y + 2,
                                Color.Red);
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X + 1, (int)contractedPoint.Y + 2,
                                Color.Red);
                            drawnEdges2.Bitmap.SetPixel((int)contractedPoint.X + 2, (int)contractedPoint.Y + 1,
                                Color.Red);
                        }
                        catch (Exception)
                        {

                        }
                    }

                    /////////////////////////////////////////////////////////////////////////////////////////////////////

                    List<CameraSpacePoint> contractedPointsCSP = new List<CameraSpacePoint>();

                    foreach (var contractedPoint in contractedPoints)
                    {
                        var depthReference =
                            pointsFromColorToDepth[(int)contractedPoint.X + (int)contractedPoint.Y * 1920];
                        if (!float.IsInfinity(depthReference.X))
                        {
                            var csp = cameraSpacePointsFromDepthData[
                                (int)depthReference.X + (int)depthReference.Y * 512];
                            contractedPointsCSP.Add(csp);
                        }
                    }



                    var contractedPointsCSPStruct =
                        contractedPointsCSP.Select(x => new MyVector3DStruct(x.X, x.Y, x.Z)).ToArray();

                    double lowestError = double.MaxValue;
                    //int eliminator = 0;
                    foreach (var csp in contractedPointsCSPStruct/*.Where(x => (eliminator++) % 2 == 0)*/)
                    {
                        // take 6 nearest neighbors
                        var neighbors = contractedPointsCSPStruct.OrderBy(
                            (MyVector3DStruct x) =>
                            {
                                return
                                    Math.Sqrt(
                                        (x.x - csp.x) * (x.x - csp.x) + (x.y - csp.y) * (x.y - csp.y) +
                                        (x.z - csp.z) * (x.z - csp.z)
                                    );
                            }).Take(7).ToArray();

                        // take all pairs
                        for (int i = 0; i < neighbors.Length; i++)
                        {
                            for (int j = i + 1; j < neighbors.Length; j++)
                            {
                                var first = neighbors[i];
                                var second = neighbors[j];

                                var firstPoint = new MyVector3DStruct(first.x, first.y, first.z);
                                var secondPoint = new MyVector3DStruct(second.x, second.y, second.z);

                                // st. point + 2 vectors
                                var cspPoint = new MyVector3DStruct(csp.x, csp.y, csp.z);
                                var firstVector = MyVector3DStruct.Difference(ref firstPoint, ref cspPoint);
                                var secondVector = MyVector3DStruct.Difference(ref secondPoint, ref cspPoint);

                                // perpendicularity check
                                double angleBetweenVectors =
                                    MyVector3DStruct.AngleInDeg(ref firstVector, ref secondVector);
                                if (!(angleBetweenVectors < 91.4 && angleBetweenVectors > 88.6))
                                {
                                    break;
                                }

                                // length check
                                double ratio = firstVector.Magnitude() / secondVector.Magnitude();
                                if (!(ratio > 0.85f && ratio < 1.15f))
                                {
                                    break;
                                }

                                // length normalization
                                double averageLength = (firstVector.Magnitude() + secondVector.Magnitude()) / 2;

                                firstVector =
                                    MyVector3DStruct.MultiplyByNumber(MyVector3DStruct.Normalize(ref firstVector),
                                        averageLength);
                                secondVector =
                                    MyVector3DStruct.MultiplyByNumber(MyVector3DStruct.Normalize(ref secondVector),
                                        averageLength);

                                var negatedFirstVector = MyVector3DStruct.Negate(ref firstVector);
                                var negatedSecondVector = MyVector3DStruct.Negate(ref secondVector);

                                // locate all possible starting points 
                                for (int f = 0; f < 9; f++)
                                {
                                    for (int s = 0; s < 9; s++)
                                    {
                                        var startingPoint =
                                            MyVector3DStruct.Addition(
                                                MyVector3DStruct.Addition(
                                                    MyVector3DStruct.MultiplyByNumber(negatedFirstVector, f),
                                                    MyVector3DStruct.MultiplyByNumber(negatedSecondVector, s)
                                                )
                                                ,
                                                cspPoint
                                            );

                                        double currentError = 0;

                                        // generate all possible chessboards for given starting point
                                        for (int ff = 0; ff < 9; ff++)
                                        {
                                            for (int ss = 0; ss < 9; ss++)
                                            {
                                                var currentPoint = MyVector3DStruct.Addition(
                                                    startingPoint,
                                                    MyVector3DStruct.Addition(
                                                        MyVector3DStruct.MultiplyByNumber(firstVector, ff),
                                                        MyVector3DStruct.MultiplyByNumber(secondVector, ss)
                                                    )
                                                );

                                                var closestPointDistance = contractedPointsCSPStruct.Min(x =>
                                                    MyVector3DStruct.Distance(ref currentPoint,
                                                        new MyVector3DStruct(x.x, x.y, x.z)));

                                                closestPointDistance =
                                                    closestPointDistance > 0.01 ? 1 : closestPointDistance;

                                                currentError += closestPointDistance;
                                            }
                                        }

                                        if (currentError < lowestError)
                                        {
                                            lowestError = currentError;
                                            startingPointFinal = startingPoint;
                                            firstVectorFinal = firstVector;
                                            secondVectorFinal = secondVector;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                RotateSpaceToChessboard(startingPointFinal, firstVectorFinal, secondVectorFinal, cameraSpacePointsFromDepthData);

                MakeSomeFiltering(cameraSpacePointsFromDepthData);
                
                
                if (state == State.AfterLocalization)
                {
                    colorImg = ReturnLocalizedChessboardWithTable(colorImg, resultBools, pointsFromColorToDepth,
                        cameraSpacePointsFromDepthData, firstVectorFinal);
                    colorImg.Bitmap.Save(@"D:\Desktop\clr2.jpeg", ImageFormat.Jpeg);
                }
                
                if (state == State.Localization)
                {
                    bmpToSend = drawnEdges2?.Bitmap;
                    // TODO DISPLAY: drawnEdges2.Bitmap
                }

                if (state == State.AfterLocalization)
                {
                    FigureLocalization(cameraSpacePointsFromDepthData, colorFrameData, pointsFromDepthToColor, infraredData, firstVectorFinal, canniedBytes);

                    bmpToSend = colorImg?.Bitmap;
                    // TODO DISPLAY: colorImg.Bitmap
                }

                if (state == State.Localization)
                {
                    state = State.AfterLocalization;
                }
            }); // end  task method  

        } // end whole method

        private Image<Rgb, byte> ReturnLocalizedChessboardWithTable(Image<Rgb, byte> colorImg, bool[] resultBools,
            DepthSpacePoint[] pointsFromColorToDepth, CameraSpacePoint[] cameraSpacePointsFromDepthData, MyVector3DStruct magnitudeVector)
        {
            Bitmap bm = colorImg.Bitmap;

            BitmapData bitmapData = bm.LockBits(new Rectangle(0, 0, bm.Width, bm.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            int bitmapSize = bm.Height * bm.Width;
            int width = bm.Width;
            int height = bm.Height;
            unsafe
            {
                byte* ptr = (byte*)bitmapData.Scan0;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int pixelPostion = (y * 1920 + x);
                        int rgbPositon = pixelPostion * 3;

                        DepthSpacePoint point = pointsFromColorToDepth[pixelPostion];
                        int pointPosition = (int)point.X + (int)point.Y * 512;

                        if (float.IsInfinity(point.X) || point.X < 0 || point.Y < 0)
                        {
                            *(ptr + rgbPositon + 2) = 255;
                            *(ptr + rgbPositon + 1) = 255;
                            *(ptr + rgbPositon + 0) = 255;
                        }
                        else
                        {
                            int colorX = (int)point.X;
                            int colorY = (int)point.Y;

                            if (colorY < 424 && colorX < 512)
                            {
                                int colorImageIndex = ((512 * colorY) + colorX);

                                if (resultBools[colorImageIndex])
                                {
                                    if (!(float.IsInfinity(cameraSpacePointsFromDepthData[pointPosition].Z) ||
                                          float.IsNaN(cameraSpacePointsFromDepthData[pointPosition].Z))

                                        && cameraSpacePointsFromDepthData[pointPosition].X > 0
                                        && cameraSpacePointsFromDepthData[pointPosition].Y > 0
                                        && cameraSpacePointsFromDepthData[pointPosition].X < magnitudeVector.Magnitude() * 8
                                        && cameraSpacePointsFromDepthData[pointPosition].Y < magnitudeVector.Magnitude() * 8
                                    )
                                    {
                                    }
                                    else
                                    {
                                        *(ptr + rgbPositon + 2) = (byte)(*(ptr + rgbPositon + 2) * 0.8f);
                                        *(ptr + rgbPositon + 1) = (byte)(*(ptr + rgbPositon + 1) * 0.8f);


                                        var value = *(ptr + rgbPositon + 0);
                                        value += (byte)((255 - value) * 0.95f);
                                        *(ptr + rgbPositon + 0) = value; // R
                                    }
                                }
                                else
                                {
                                    *(ptr + rgbPositon + 2) = 255;
                                    *(ptr + rgbPositon + 1) = 255;
                                    *(ptr + rgbPositon + 0) = 255;
                                }
                            }

                        }
                    }
                }
            }
            bm.UnlockBits(bitmapData);
            

            colorImg.Bitmap = bm;

            return colorImg;
        }


        private Image<Rgb, byte> ReturnColorImageOfTable(bool[] resultBools, byte[] colorFrameData, DepthSpacePoint[] pointsFromColorToDepth)
        {

            //Image<Rgb, byte> colorImg = new Image<Rgb, byte>(kinect.ColorFrameDescription.Width, kinect.ColorFrameDescription.Height);
            Image<Rgb, byte> colorImg = new Image<Rgb, byte>(1920, 1080);

            Bitmap bm = new Bitmap(1920, 1080, PixelFormat.Format24bppRgb);

            BitmapData bitmapData = bm.LockBits(new Rectangle(0, 0, bm.Width, bm.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            int bitmapSize = bm.Height * bm.Width;
            int width = bm.Width;
            int height = bm.Height;
            unsafe
            {
                byte* ptr = (byte*)bitmapData.Scan0;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int pixelPostion = (y * 1920 + x);
                        int rgbPositon = pixelPostion * 3;

                        DepthSpacePoint point = pointsFromColorToDepth[pixelPostion];

                        if (float.IsInfinity(point.X) || point.X < 0 || point.Y < 0)
                        {
                            *(ptr + rgbPositon + 2) = 255;
                            *(ptr + rgbPositon + 1) = 255;
                            *(ptr + rgbPositon + 0) = 255;
                        }
                        else
                        {
                            int colorX = (int)point.X;
                            int colorY = (int)point.Y;

                            if (colorY < 424 && colorX < 512)
                            {
                                int colorImageIndex = ((512 * colorY) + colorX);

                                if (resultBools[colorImageIndex])
                                {
                                    *(ptr + rgbPositon + 2) = (byte)((colorFrameData[pixelPostion * 4]));
                                    *(ptr + rgbPositon + 1) = (byte)((colorFrameData[pixelPostion * 4 + 1]));
                                    *(ptr + rgbPositon + 0) = (byte)((colorFrameData[pixelPostion * 4 + 2]));
                                }
                                else
                                {
                                    *(ptr + rgbPositon + 2) = 255;
                                    *(ptr + rgbPositon + 1) = 255;
                                    *(ptr + rgbPositon + 0) = 255;
                                }
                            }

                        }
                    }
                }
            }
            bm.UnlockBits(bitmapData);



            /////////////////////////////////////////////////////////

            BitmapData bmpdata = null;

            try
            {
                bmpdata = bm.LockBits(new Rectangle(0, 0, bm.Width, bm.Height), ImageLockMode.ReadOnly, bm.PixelFormat);
                int numbytes = bmpdata.Stride * bm.Height;
                byte[] bytedata = new byte[numbytes];
                IntPtr ptr = bmpdata.Scan0;

                Marshal.Copy(ptr, bytedata, 0, numbytes);

                colorImg.Bytes = bytedata;
            }
            finally
            {
                if (bmpdata != null)
                    bm.UnlockBits(bmpdata);
            }

            return colorImg;
        }

        private byte[] CannyAppliedToDepthData(CameraSpacePoint[] cameraSpacePointsFromDepthData)
        {
            int depthColorChange = 128;

            Bitmap bbb = new Bitmap(512, 424, PixelFormat.Format24bppRgb);
            BitmapData bbbData = bbb.LockBits(new Rectangle(0, 0, bbb.Width, bbb.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

            unsafe
            {
                byte* ptr = (byte*)bbbData.Scan0;

                // draw pixel according to its type
                for (int y = 0; y < 424; y++)
                {
                    // compensates sensors natural flip of image
                    for (int x = 0; x < 512; x++)
                    {
                        int position = y * 512 + x;

                        byte value = (byte)(cameraSpacePointsFromDepthData[position].Z * depthColorChange);

                        *ptr++ = value;
                        *ptr++ = value;
                        *ptr++ = value;

                    }
                }
            }

            bbb.UnlockBits(bbbData);

            //////////////////
            Image<Gray, byte> CanniedImage = new Image<Gray, byte>(bbb);
            CanniedImage = CanniedImage.Canny(1000, 1200, 7, true).SmoothGaussian(3, 3, 1, 1).ThresholdBinary(new Gray(65), new Gray(255));
            var canniedBytes = CanniedImage.Bytes;
            return canniedBytes;
        }

        private void FigureLocalization(CameraSpacePoint[] cameraSpacePointsFromDepthData, byte[] colorFrameData, ColorSpacePoint[] pointsFromDepthToColor, ushort[] infraredData,
                                            MyVector3DStruct magnitudeVector, byte[] canniedBytes)
        {
            List<RGBcolor>[,] loc = new List<RGBcolor>[8, 8];
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    loc[x, y] = new List<RGBcolor>();
                }
            }

            for (int i = 0; i < cameraSpacePointsFromDepthData.Length; i++)
            {
                if (!(float.IsInfinity(cameraSpacePointsFromDepthData[i].Z) || float.IsNaN(cameraSpacePointsFromDepthData[i].Z))

                && cameraSpacePointsFromDepthData[i].X > 0
                && cameraSpacePointsFromDepthData[i].Y > 0
                && cameraSpacePointsFromDepthData[i].X < magnitudeVector.Magnitude() * 8
                && cameraSpacePointsFromDepthData[i].Y < magnitudeVector.Magnitude() * 8

                && infraredData[i] > 1500

                && canniedBytes[i] != 255

                //&& cameraSpacePointsFromDepthData[i].Z < 0.025f
                && cameraSpacePointsFromDepthData[i].Z < -0.01f
                && cameraSpacePointsFromDepthData[i].Z > -0.5f
                )
                {
                    var reference = pointsFromDepthToColor[i];

                    if (reference.X > 0 && reference.X < 1920 && reference.Y > 0 && reference.Y < 1080)
                    {
                        var r = colorFrameData[((int)reference.X + (int)reference.Y * 1920) * 4 + 0];
                        var g = colorFrameData[((int)reference.X + (int)reference.Y * 1920) * 4 + 1];
                        var b = colorFrameData[((int)reference.X + (int)reference.Y * 1920) * 4 + 2];

                        int x = (int)Math.Floor(cameraSpacePointsFromDepthData[i].X / magnitudeVector.Magnitude());
                        int y = (int)Math.Floor(cameraSpacePointsFromDepthData[i].Y / magnitudeVector.Magnitude());

                        if (x >= 0 && y >= 0 && x < 8 && y < 8)
                        {
                            /*
                            if (r > 80 && g > 80)
                            {
                                loc[x, y].Add(FigureColor.White);
                            }
                            else
                            {
                                loc[x, y].Add(FigureColor.Black);
                            }
                            */
                            loc[x, y].Add(new RGBcolor(r, g, b));
                        }

                    }

                }
            }

            var bm = new Bitmap(320, 320, PixelFormat.Format24bppRgb);
            SolidBrush blackBrush = new SolidBrush(Color.Black);
            SolidBrush whiteBrush = new SolidBrush(Color.White);
            SolidBrush blueBrush = new SolidBrush(Color.LightSkyBlue);

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    using (Graphics graphics = Graphics.FromImage(bm))
                    {
                        if (loc[x, y].Count < 5)
                        {
                            graphics.FillRectangle(blueBrush, new Rectangle(x * 40, y * 40, 40, 40));
                            //bm.SetPixel(x, y, Color.LightSkyBlue);
                        }
                        else
                        {
                            byte avgRed = (byte)(loc[x, y].Sum(f => f.g) / loc[x, y].Count);
                            byte avgGreen = (byte)(loc[x, y].Sum(f => f.g) / loc[x, y].Count);
                            byte avgBlue = (byte)(loc[x, y].Sum(f => f.b) / loc[x, y].Count);

                            var brightness = Color.FromArgb(avgRed, avgGreen, avgBlue).GetBrightness();

                            if (brightness > 0.47) // 41
                            {
                                graphics.FillRectangle(whiteBrush, new Rectangle(x * 40, y * 40, 40, 40));
                                //bm.SetPixel(x, y, Color.White);
                            }
                            else
                            {
                                graphics.FillRectangle(blackBrush, new Rectangle(x * 40, y * 40, 40, 40));
                                //bm.SetPixel(x, y, Color.Black);
                            }
                        }

                    }
                }
            }
            /*
            SendResultMessage(
                new ResultMessage(
                        bmpToSend,
                        bm,
                    "whatever"
            ));*/
            //DISPLAY: FormLocations.Image
        }

        private void LogExtractedChessboard(CameraSpacePoint[] cameraSpacePointsFromDepthData, byte[] colorFrameData, ColorSpacePoint[] pointsFromDepthToColor, ushort[] infraredData,
                                            MyVector3DStruct magnitudeVector, byte[] canniedBytes)
        {
            StreamWriter swRot = new StreamWriter(@"D:\Desktop\" + DateTime.Now.ToString("OR" + "RR_dd-yyyy_HH-MM-ss-fff", CultureInfo.InvariantCulture) + ".xyz");

            for (int i = 0; i < cameraSpacePointsFromDepthData.Length; i++)
            {
                if (!(float.IsInfinity(cameraSpacePointsFromDepthData[i].Z) || float.IsNaN(cameraSpacePointsFromDepthData[i].Z))

                && cameraSpacePointsFromDepthData[i].X > 0
                && cameraSpacePointsFromDepthData[i].Y > 0
                && cameraSpacePointsFromDepthData[i].X < magnitudeVector.Magnitude() * 8
                && cameraSpacePointsFromDepthData[i].Y < magnitudeVector.Magnitude() * 8

                && infraredData[i] > 1500

                && canniedBytes[i] != 255

                //&& cameraSpacePointsFromDepthData[i].Z < 0.025f
                && cameraSpacePointsFromDepthData[i].Z < -0.01f
                && cameraSpacePointsFromDepthData[i].Z > -0.5f
                )
                {
                    var reference = pointsFromDepthToColor[i];

                    if (reference.X > 0 && reference.X < 1920 && reference.Y > 0 && reference.Y < 1080)
                    {
                        var r = colorFrameData[((int)reference.X + (int)reference.Y * 1920) * 4 + 0];
                        var g = colorFrameData[((int)reference.X + (int)reference.Y * 1920) * 4 + 1];
                        var b = colorFrameData[((int)reference.X + (int)reference.Y * 1920) * 4 + 2];

                        if (
                            /*
                            !(r > 140 &&
                            g > 140 &&
                            b > 180) 
                            &&
                            */
                            true
                            )
                        {
                            swRot.WriteLine(
                                string.Format(
                                    "{0} {1} {2} {3} {4} {5}",
                                    (cameraSpacePointsFromDepthData[i].X).ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture),
                                    (cameraSpacePointsFromDepthData[i].Y).ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture),
                                    (cameraSpacePointsFromDepthData[i].Z).ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture),
                                    b,
                                    g,
                                    r
                                )
                            );
                        }

                    }

                }
            }

            swRot.Close();

            StreamWriter swRotM = new StreamWriter(@"D:\Desktop\" + DateTime.Now.ToString("ORGGG" + "RR_dd-yyyy_HH-MM-ss-fff", CultureInfo.InvariantCulture) + ".xyz");

            for (int x = 0; x < 9; x++)
            {
                for (int y = 0; y < 9; y++)
                {
                    swRotM.WriteLine(
                        string.Format(
                            "{0} {1} {2} {3} {4} {5}",
                            (magnitudeVector.Magnitude() * x).ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture),
                            (magnitudeVector.Magnitude() * y).ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture),
                            (0).ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture),
                            0,
                            255,
                            0
                        )
                    );
                }
            }
            swRotM.Close();
        }

        public void RotateSpaceToChessboard(MyVector3DStruct startingPointFinal, MyVector3DStruct firstVectorFinal, MyVector3DStruct secondVectorFinal, CameraSpacePoint[] cspFromdd)
        {
            firstVectorFinal = MyVector3DStruct.Normalize(firstVectorFinal);
            secondVectorFinal = MyVector3DStruct.Normalize(secondVectorFinal);

            var a = MyVector3DStruct.CrossProduct(ref firstVectorFinal, ref secondVectorFinal);
            var b = MyVector3DStruct.CrossProduct(ref secondVectorFinal, ref firstVectorFinal);
            var xVec = new MyVector3DStruct();
            var yVec = new MyVector3DStruct();
            var zVec = new MyVector3DStruct();



            // get new base based on cross product direction
            if ((a.z > 0 && b.z < 0)) // puvodne (a.z > 0 && b.z < 0)
            {
                zVec = a; // a
                yVec = MyVector3DStruct.Normalize(MyVector3DStruct.CrossProduct(ref xVec, ref zVec));

            }
            else if ((a.z < 0 && b.z > 0)) // puvodne (a.z < 0 && b.z > 0)
            {
                zVec = b; // b
                yVec = MyVector3DStruct.Normalize(MyVector3DStruct.CrossProduct(ref xVec, ref zVec));
            }
            else
            {
                throw new OutOfMemoryException();
            }

            xVec = MyVector3DStruct.Normalize(firstVectorFinal);
            yVec = MyVector3DStruct.Normalize(secondVectorFinal);
            zVec = MyVector3DStruct.Normalize(zVec);

            // spočítat inverzní matici

            double[,] matrix =
            {

                {xVec.x, yVec.x, zVec.x},
                {xVec.y, yVec.y, zVec.y},
                {xVec.z, yVec.z, zVec.z}
                
                /*
                {xVec.x, xVec.y, xVec.z},
                {yVec.x, yVec.y, yVec.z},
                {zVec.x, zVec.y, zVec.z}
                */
                
            };
            var inverseMatrix = matrix.Inverse();

            for (int i = 0; i < cspFromdd.Length; i++)
            {
                var nx = (float)(cspFromdd[i].X - startingPointFinal.x);
                var ny = (float)(cspFromdd[i].Y - startingPointFinal.y);
                var nz = (float)(cspFromdd[i].Z - startingPointFinal.z);


                cspFromdd[i].X = (float)(inverseMatrix[0, 0] * nx + inverseMatrix[0, 1] * ny + inverseMatrix[0, 2] * nz);
                cspFromdd[i].Y = (float)(inverseMatrix[1, 0] * nx + inverseMatrix[1, 1] * ny + inverseMatrix[1, 2] * nz);
                cspFromdd[i].Z = (float)(inverseMatrix[2, 0] * nx + inverseMatrix[2, 1] * ny + inverseMatrix[2, 2] * nz);

                /*
                cspFromdd[i].X = (float)(inverseMatrix[0, 0] * cspFromdd[i].X + inverseMatrix[1, 0] * cspFromdd[i].Y + inverseMatrix[2, 0] * cspFromdd[i].Z);
                cspFromdd[i].Y = (float)(inverseMatrix[0, 1] * cspFromdd[i].X + inverseMatrix[1, 1] * cspFromdd[i].Y + inverseMatrix[2, 1] * cspFromdd[i].Z);
                cspFromdd[i].Z = (float)(inverseMatrix[0, 2] * cspFromdd[i].X + inverseMatrix[1, 2] * cspFromdd[i].Y + inverseMatrix[2, 2] * cspFromdd[i].Z);
                */
            }

        }

       

        private void MakeSomeFiltering(CameraSpacePoint[] cameraSpacePointsFromDepthData)
        {
            float[,,] channelsImage = new float[512, 424, 1];

            for (int y = 0; y < 424; y++)
            {
                for (int x = 0; x < 512; x++)
                {
                    if (!float.IsInfinity(cameraSpacePointsFromDepthData[y * 512 + x].Z))
                    {
                        channelsImage[x, y, 0] = cameraSpacePointsFromDepthData[y * 512 + x].Z;
                    }
                    else
                    {
                        channelsImage[x, y, 0] = -10000;
                    }
                }
            }

            Image<Gray, float> imgToFilter = new Image<Gray, float>(channelsImage);


            var filteredImage = imgToFilter;//.ThresholdBinaryInv(new Gray(-0.005f),new Gray(0.03));//.Dilate(2);
            /*
            float[,] k = { {-0.2f, -0.2f, -0.2f},
                {-0.2f, 2.8f, -0.2f},
                {-0.2f, -0.2f, -0.2f}};
            ConvolutionKernelF kernel = new ConvolutionKernelF(k);
            var filteredImage = imgToFilter * kernel;

            */
            var fiteredData = filteredImage.Data;

            for (int y = 0; y < 424; y++)
            {
                for (int x = 0; x < 512; x++)
                {
                    if (fiteredData[x, y, 0] > -5000)
                    {
                        cameraSpacePointsFromDepthData[y * 512 + x].Z = fiteredData[x, y, 0];
                    }
                    else
                    {
                        cameraSpacePointsFromDepthData[y * 512 + x].Z = float.PositiveInfinity;
                    }
                }
            }
        }

        private Tuple<LineSegment2D[], LineSegment2D[]> FilterLinesBasedOnAngle(LineSegment2D[] lines, int angle)
        {
            int deg = 180;
            var resultLines1 = new List<LineSegment2D>();
            var resultLines2 = new List<LineSegment2D>();

            List<LineSegment2D>[] linesByAngle = new List<LineSegment2D>[deg];
            for (int i = 0; i < linesByAngle.Length; i++)
            {
                linesByAngle[i] = new List<LineSegment2D>();
            }

            // fill lines into their degree reprezentation
            for (int i = 0; i < lines.Length; i++)
            {
                var diffX = lines[i].P1.X - lines[i].P2.X;
                var diffY = lines[i].P1.Y - lines[i].P2.Y;

                int theta = Mod(((int)ConvertRadiansToDegrees(Math.Atan2(diffY, diffX))), deg);
                linesByAngle[theta].Add(lines[i]);
            }

            // get first max window
            int maxNumber = -1;
            int maxIndex = -1;

            for (int i = 0; i < deg; i++)
            {
                int number = 0;
                int index = i;

                for (int j = i; j < i + angle; j++)
                {
                    number += linesByAngle[Mod(j, deg)].Count;
                }

                if (number > maxNumber)
                {
                    maxNumber = number;
                    maxIndex = index;
                }
            }

            // fill and remove
            for (int j = maxIndex; j < maxIndex + angle; j++)
            {
                var linesOfCertainAngle = linesByAngle[Mod(j, deg)];

                foreach (var lineOfCeratinAngle in linesOfCertainAngle)
                {
                    resultLines1.Add(lineOfCeratinAngle);
                }

                linesByAngle[Mod(j, deg)].Clear();
            }

            // get second max window
            maxNumber = -1;
            maxIndex = -1;

            for (int i = 0; i < deg; i++)
            {
                int number = 0;
                int index = i;

                for (int j = i; j < i + angle; j++)
                {
                    number += linesByAngle[Mod(j, deg)].Count;
                }

                if (number > maxNumber)
                {
                    maxNumber = number;
                    maxIndex = index;
                }
            }

            // fill and remove
            for (int j = maxIndex; j < maxIndex + angle; j++)
            {
                var linesOfCertainAngle = linesByAngle[Mod(j, deg)];

                foreach (var lineOfCeratinAngle in linesOfCertainAngle)
                {
                    resultLines2.Add(lineOfCeratinAngle);
                }

                linesByAngle[Mod(j, deg)].Clear();
            }


            return new Tuple<LineSegment2D[], LineSegment2D[]>(resultLines1.ToArray(), resultLines2.ToArray());
        }

        public static double ConvertRadiansToDegrees(double radians)
        {
            double degrees = (180 / Math.PI) * radians;
            return (degrees);
        }

        int Mod(int x, int m)
        {
            return (x % m + m) % m;
        }

        Random rnd = new Random();
        
        enum State { BeforeLocalization, Localization, AfterLocalization, Running };
        

        private struct RGBcolor
        {
            public byte r;
            public byte g;
            public byte b;

            public RGBcolor(byte r, byte g, byte b)
            {
                this.r = r;
                this.g = g;
                this.b = b;
            }
        }

        private void SendResultMessage(Message msg)
        {
            ProcessingOutputQueue.Add(msg);
        }
    }
}
