using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace WindowsFormsApp1
{
    class Coin
    {
        public int MinX, MaxX, MinY, MaxY, Area;
        public double Diameter;
        public bool HasHole;
        public int Type = -1; // 0=5c(hole) 1=10c 2=25c 3=P1 4=P5
    }
}
