﻿namespace ChessTracking.ImageProcessing.PipelineData
{
    /// <summary>
    /// Data arriving into pipeline
    /// </summary>
    class InputData
    {
        public KinectData KinectData { get; set; }
        public TrackingResultData ResultData { get; set; }
        public UserDefinedParameters UserParameters { get; set; }
        
        public InputData(KinectData kinectData, UserDefinedParameters userParameters)
        {
            KinectData = kinectData;
            UserParameters = userParameters;
            ResultData = new TrackingResultData();
        }
    }
}
