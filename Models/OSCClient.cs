using SharpOSC;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CPRTouchVision.Models
{
    internal class OSCClient
    {
        UDPSender[] _senders = new UDPSender[2];
        UDPSender _sender;


        public OSCClient()
        {
            for (int i = 0; i < 2; i++)
                _senders[i] = new UDPSender("127.0.0.1", 39539 + i);

            _sender = new UDPSender("127.0.0.1", 1488);
        }

        public void Send(byte[] bytes)
        {
            _sender.Send(new OscMessage("/cpr/body", bytes));
        }

        public void Send(List<TouchEvent> touches) 
        {

                // Build one string that includes total count and all touch data
                string payload = $"count:{touches.Count};" + string.Join(";", touches.Select(t =>
                    $"id:{t.Id},x:{t.X:F2},y:{t.Y:F2},r:{t.Radius:F2},timestamp:{t.Timestamp.Ticks}"));

                // Convert string to bytes
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(payload);

                // Send as individual OSC message per touch
                _sender.Send(new OscMessage("/cpr/touch", bytes));
            
        }
    }
}
