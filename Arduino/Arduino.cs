using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Ports;
using System.Diagnostics;

namespace Uctrl.Arduino
{
    public class Arduino : IDisposable
    {
        private SerialPort serialPort;

        public Arduino()
        {
            serialPort = new SerialPort();
        }

        public bool OpenPort(string port)
        {
            try
            {
                serialPort.PortName = port;
                serialPort.BaudRate = 115200;
                serialPort.ReadTimeout = 1000;
                serialPort.WriteTimeout = 1000;
				serialPort.Handshake = Handshake.None;
                serialPort.NewLine = "\n";
                serialPort.Encoding = Encoding.GetEncoding("ISO-8859-1");
                serialPort.Open();

                return serialPort.IsOpen;
            }

            catch (IOException)
            {
                return false;
            }
        }

        public SerialPort Port
        {
            get { return serialPort; }
        }

        public void Dispose()
        {
            serialPort.Close();
            serialPort.Dispose();
        }

        public bool Send(string command)
        {
            if (!Port.IsOpen) return false;

            try
            {
                //string response = string.Empty;

                serialPort.WriteLine(command);

                //response = serialPort.ReadLine();

                //Debug.WriteLine("{0} --> {1}", string.Join(",", rgb), response);

                return true;
            }

            catch (IOException)
            {
                return false;
            }
        }

        public bool SetLEDs(byte[] colors)
        {
            return Send("rgb:" + string.Join(",", colors));
        }

        public bool SetCallerId(string callerId)
        {
            return Send("callerid:" + callerId);
        }
		
        public bool ClearCallerId()
        {
            return Send("callerid:");
        }
    }
}
