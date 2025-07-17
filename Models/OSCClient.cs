using System;
using System.Collections.Generic;
using SharpOSC;

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

        public void Send() // List<Body> bodies
        {
            // TODO: - Replace with new touch event logic here

            //var list = new List<OscMessage>();

            //foreach (var body in bodies)
            //{
            //    list.Add(new OscMessage("/cpr/body", body.ToBytes()));
            //}

            //var bundle = new OscBundle((ulong)DateTimeOffset.Now.ToUnixTimeSeconds(), list.ToArray());
            //_sender.Send(bundle);
        }
    }
}
