﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ChessTracking.Game.Figures;
using ChessTracking.MultithreadingMessages;

namespace ChessTracking.Game
{
    class GameData
    {
        public Figure[,] Figures { get; set; }
        public bool IsWhitePlaying { get; set; }

        public GameData(Figure[,] figures, bool isWhitePlaying)
        {
            this.Figures = figures;
            this.IsWhitePlaying = isWhitePlaying;
        }

        public TrackingState GetTrackingStates()
        {
            var figures = new TrackingFieldState[8,8];

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    if (Figures[x,y] == null)
                    {
                        figures[x, y] = TrackingFieldState.None;
                    }
                    else if (Figures[x,y].IsWhite)
                    {
                        figures[x, y] = TrackingFieldState.White;
                    }
                    else
                    {
                        figures[x, y] = TrackingFieldState.Black;
                    }
                }
            }

            var state = new TrackingState(figures);
            return state;
        }
    }
}