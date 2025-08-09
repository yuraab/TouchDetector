using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public class TouchEvent
    {
        public float X { get; set; }
        public float Y { get; set; }
        public int Id { get; set; }
        public float Radius { get; set; }
        public DateTime Timestamp { get; set; }

        public override string ToString()
        {
            return $"Touch(Id:{Id}, X:{X:F2}, Y:{Y:F2}, Radius:{Radius:F2}, timestamp:{Timestamp})";
        }
    }
    public class HomographyTouchEvent
    {
        public float X { get; set; }
        public float Y { get; set; }
        public int Id { get; set; }
        public float Radius { get; set; }

        public override string ToString()
        {
            return $"Touch(Id:{Id}, X:{X:F2}, Y:{Y:F2}, Radius:{Radius:F2})";
        }
    }
}
