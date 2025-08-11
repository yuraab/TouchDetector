using SharpOSC;
using System;
using System.Collections.Generic;
//using System.Collections.Specialized;
//using System.Linq;
//using System.Net;

namespace CPRTouchVision.Models
{
    internal class OSCClient
    {

        private int _id = 0;
        private string endPoint = "/tuio/2Dcur";
        public EventHandler<List<TouchEvent>>? Sent;
        private UDPSender _sender;

        public OSCClient(string ip = "127.0.0.1", int port = 3333)
        {

            _sender = new UDPSender(ip, port);
        }

        public void Send(byte[] bytes)
        {
            _sender.Send(new OscMessage(endPoint, bytes));
        }

        public void Send(List<TouchEvent> touches) 
        {
            var aliveMessage = new OscMessage(endPoint, "alive");
            var setBundle = new OscBundle(OscTimeHelper.ToNTPTimestamp(DateTime.UtcNow));
            var fseqMessage = new OscMessage(endPoint, "fseq", -1);

            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                aliveMessage.Arguments.Add(_id);
                setBundle.Messages.Add(new OscMessage(endPoint, "set", _id, touch.NormalizedX, touch.NormalizedY, 0.0f, 0.0f, 0.0f));
                if(_id == int.MaxValue)
                {
                    _id = 0;
                }
                else
                {
                    _id++;
                }
            }
            _sender.Send(aliveMessage);
            _sender.Send(setBundle);
            _sender.Send(fseqMessage);
            Sent?.Invoke(this, touches);
        }
    }

    public static class OscTimeHelper
    {
        public static ulong ToNTPTimestamp(this DateTime dt)
        {
            var ntpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var seconds = (dt.ToUniversalTime() - ntpEpoch).TotalSeconds;

            ulong intPart = (ulong)seconds;
            ulong fracPart = (ulong)((seconds - intPart) * 0x100000000L); // 2^32

            return (intPart << 32) | fracPart;
        }
    }

}
