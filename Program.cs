using System;
using System.Net.Sockets;
using System.Threading;
using MissionPlanner.Comms;
using UAVCAN;

namespace CanPassThrough
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Usage: CanPassThrough.exe serialport tcpport");

            var comport = args[0];
            var tcpport = args[1];

            var serial = new SerialPort(comport, 115200);
            serial.Open();

            var can = new UAVCAN.uavcan();

            can.SetupDynamicNodeAllocator();
            can.PrintDebugToConsole();
            can.StartSLCAN(serial.BaseStream);

            TcpListener listener = new TcpListener(int.Parse(tcpport));
            listener.Start();

            int tcpbps = 0;
            int rtcmbps = 0;
            int combps = 0;
            int second = 0;

            while (true)
            {
                var client = listener.AcceptTcpClient();
                client.NoDelay = true;
                client.SendBufferSize = 1024;
                client.ReceiveBufferSize = 1024;

                var st = client.GetStream();

                can.MessageReceived += (frame, msg, id) =>
                {
                    combps += frame.SizeofEntireMsg;
                    if (frame.MsgTypeID == UAVCAN.uavcan.UAVCAN_EQUIPMENT_GNSS_RTCMSTREAM_DT_ID)
                    {
                        var data = msg as uavcan.uavcan_equipment_gnss_RTCMStream;
                        try
                        {
                            rtcmbps += data.data_len;
                            st.Write(data.data, 0, data.data_len);
                            st.Flush();
                        }
                        catch
                        {
                            client = null;
                        }
                    }
                };

                try
                {
                    while (true)
                    {
                        if (client.Available > 0)
                        {
                            var toread = Math.Min(client.Available, 128);
                            byte[] buffer = new byte[toread];
                            var read = st.Read(buffer, 0, toread);
                            foreach (var b in buffer)
                            {
                                Console.Write("0x{0:X} ", b);
                            }
                            Console.WriteLine();
                            tcpbps += read;
                            var slcan = can.PackageMessage(0, 0, 0,
                                new uavcan.uavcan_equipment_gnss_RTCMStream()
                                    {protocol_id = 3, data = buffer, data_len = (byte) read});
                            can.WriteToStream(slcan);
                            serial.BaseStream.Flush();
                        }

                        Thread.Sleep(1);

                        if (second != DateTime.Now.Second)
                        {
                            Console.WriteLine("tcp:{0} can:{1} data:{2} avail:{3}", tcpbps, combps, rtcmbps, client.Available);
                            tcpbps = combps = rtcmbps = 0;
                            second = DateTime.Now.Second;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }
    }
}
