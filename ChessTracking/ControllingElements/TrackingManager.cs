﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using ChessTracking.Forms;
using ChessTracking.MultithreadingMessages;
using ChessTracking.ProcessingElements;

namespace ChessTracking.ControllingElements
{
    class TrackingManager
    {
        public MainGameForm GameForm { get; }
        public BlockingCollection<Message> ProcessingCommandsQueue { get; set; }
        public BlockingCollection<Message> ProcessingOutputQueue { get; set; }

        public TrackingManager(MainGameForm mainGameForm)
        {
            this.GameForm = mainGameForm;
            ProcessingCommandsQueue = new BlockingCollection<Message>();
            ProcessingOutputQueue = new BlockingCollection<Message>();
        }

        public void StartTracking()
        {
            Task.Run(() =>
            {
                var processingController = new ProcessingController(ProcessingCommandsQueue, ProcessingOutputQueue);
                processingController.Start();
            });
        }

        public void StopTracking()
        {
            ProcessingCommandsQueue.Add(new CommandMessage());
        }

        public void ProcessQueue()
        {
            bool messageProcessed = false;

            do
            {
                messageProcessed = ProcessingOutputQueue.TryTake(out var message, TimeSpan.FromMilliseconds(1));

                if (message is ResultMessage resultMessage)
                {
                    GameForm.DisplayVizuaization(resultMessage.BitmapToDisplay);
                }
            } while (messageProcessed);
        }
    }
}
