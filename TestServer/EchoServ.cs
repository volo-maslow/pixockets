﻿using Pixockets;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using Pixockets.Pools;

namespace TestServer
{
    public class EchoServ : SmartReceiverBase
    {
        private SmartSock _servSock;
        private BufferPoolBase _bufferPool;
        private readonly ConcurrentDictionary<IPEndPoint, int> _clients = new ConcurrentDictionary<IPEndPoint, int>();
        private readonly Stopwatch _timer = new Stopwatch();

        public EchoServ()
        {
            _timer.Start();
        }

        public void SetSocket(SmartSock socket, BufferPoolBase bufferPool)
        {
            _servSock = socket;
            _bufferPool = bufferPool;
        }

        public void OnReceive(byte[] buffer, int offset, int length, IPEndPoint endPoint, bool inOrder)
        {
            if (!inOrder)
            {
                Console.WriteLine("!!! OutOfOrder !!!");
                return;
            }

            int cliVal;

            if (!_clients.TryGetValue(endPoint, out cliVal))
            {
                Console.WriteLine("!!! Received packet from not connected !!!: {0}:{1}", endPoint.Address, endPoint.Port);
                return;
            }

            if (cliVal == 0)
            {
                var count = BitConverter.ToInt32(buffer, offset);
                Console.WriteLine("Received initial packet with {0} numbers", count);
                for (int i = 0; i < count; ++i)
                {
                    var num = BitConverter.ToInt32(buffer, offset + 4 + i * 4);
                    if (num != i)
                    {
                        Console.WriteLine("Error in initial packet at position {0}*4", i + 1);
                        break;
                    }
                }
                _clients[endPoint] = 1;
            }
            else
            {
                var count = BitConverter.ToInt32(buffer, offset);
                _clients[endPoint] = count;

                for (int i = 0; i < count; ++i)
                {
                    var num = BitConverter.ToInt32(buffer, offset + 4 + i * 4);
                    if (num != i)
                    {
                        Console.WriteLine("Error in packet at position {0}*4", i + 1);
                        break;
                    }
                }
            }

            //Console.WriteLine("Received: {0}:{1}:{2}", endPoint.Address, endPoint.Port, _clients[endPoint]);
        }

        public void Tick()
        {
            _servSock.Tick();
            var packet = new ReceivedSmartPacket();
            while (true)
            {
                if (_servSock.Receive(ref packet))
                {
                    OnReceive(packet.Buffer, packet.Offset, packet.Length, packet.EndPoint, packet.InOrder);
                    _bufferPool.Put(packet.Buffer);
                }
                else
                {
                    break;
                }
            }

            var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(_clients.Count), 0, 4);
            foreach (var client in _clients)
            {
                ms.Write(BitConverter.GetBytes(client.Value), 0, 4);
            }
            var sendBuffer = ms.ToArray();
            Broadcast(sendBuffer, 0, sendBuffer.Length);
        }

        public void Broadcast(byte[] buffer, int offset, int length)
        {
            foreach (var client in _clients)
            {
                _servSock.Send(client.Key, buffer, offset, length, true);
            }
        }

        public override void OnConnect(IPEndPoint endPoint)
        {
            Console.WriteLine("Connected: {0}:{1}", endPoint.Address, endPoint.Port);
            _clients.TryAdd(endPoint, 0);
        }

        public override void OnDisconnect(IPEndPoint endPoint)
        {
            Console.WriteLine("Disconnected: {0}:{1}", endPoint.Address, endPoint.Port);
            int ts;
            _clients.TryRemove(endPoint, out ts);
        }
    }
}
