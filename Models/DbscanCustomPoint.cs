using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public class DbscanCustomPoint
    {
        public double[] Point { get; set; }  // 3D point: [x, y, z]
        public bool Visited { get; set; } = false;
        public int? ClusterId { get; set; } = null;

        public DbscanCustomPoint(double x, double y, double z)
        {
            Point = new[] { x, y, z };
        }

    }
}
