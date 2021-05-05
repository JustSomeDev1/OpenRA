#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace OpenRA.Server
{
	public class Connection : IDisposable
	{
		public const int MaxOrderLength = 131072;

		public readonly Socket Socket;
		public readonly List<byte> Data = new List<byte>();
		public readonly int PlayerIndex;
		public readonly string AuthToken;

		public long TimeSinceLastResponse => Game.RunTime - lastReceivedTime;
		public int MostRecentFrame { get; private set; }

		public bool TimeoutMessageShown;
		public bool Validated;

		ReceiveState state = ReceiveState.Header;
		int expectLength = 8;
		int frame = 0;
		long lastReceivedTime = 0;

		readonly Thread sendThread;
		readonly CancellationTokenSource sendCancellationToken = new CancellationTokenSource();
		readonly BlockingCollection<byte[]> sendQueue = new BlockingCollection<byte[]>();
		volatile Stopwatch currentSendElapsed;

		public Connection(Socket socket, int playerIndex, string authToken)
		{
			Socket = socket;
			PlayerIndex = playerIndex;
			AuthToken = authToken;

			// Spawn a dedicated thread for sending data to this connection.
			// This allows us to detect and drop the client if sending has blocked for too long.
			sendThread = new Thread(SendThreadLoop)
			{
				Name = $"Connection send thread ({Socket.RemoteEndPoint})",
				IsBackground = true
			};

			sendThread.Start(sendCancellationToken.Token);
		}

		void SendThreadLoop(object obj)
		{
			var token = (CancellationToken)obj;
			try
			{
				while (true)
				{
					// Wait for some data to send
					// OperationCanceledException will be throw if the cancellation token is canceled, exiting the loop
					var data = sendQueue.Take(token);
					currentSendElapsed = Stopwatch.StartNew();
					Socket.Send(data);
					currentSendElapsed = null;
				}
			}
			catch (Exception e)
			{
				Log.Write("server", $"Sending to {Socket.RemoteEndPoint} failed with error {e}");
			}

			// Clean up the socket
			try
			{
				if (token.IsCancellationRequested)
					Socket.Close();
				else
					Socket.Shutdown(SocketShutdown.Send);
			}
			catch { }
		}

		public void SendDataAsync(byte[] data)
		{
			if (!sendThread.IsAlive)
				throw new Exception($"Connection send thread ({Socket.RemoteEndPoint}) is no longer alive.");

			// Take a copy of the timer to avoid a race with the send thread
			var sendElapsed = currentSendElapsed;
			if (sendElapsed != null && sendElapsed.ElapsedMilliseconds > 10000)
				throw new Exception("Connection send thread ({Socket.RemoteEndPoint}) blocked for ${sendElapsed.ElapsedMilliseconds}ms");

			sendQueue.Add(data);
		}

		public byte[] PopBytes(int n)
		{
			var result = Data.GetRange(0, n);
			Data.RemoveRange(0, n);
			return result.ToArray();
		}

		bool ReadDataInner(Server server)
		{
			var rx = new byte[1024];
			var len = 0;

			while (true)
			{
				try
				{
					// Poll the socket first to see if there's anything there.
					// This avoids the exception with SocketErrorCode == `SocketError.WouldBlock` thrown
					// from `socket.Receive(rx)`.
					if (!Socket.Poll(0, SelectMode.SelectRead)) break;

					if ((len = Socket.Receive(rx)) > 0)
						Data.AddRange(rx.Take(len));
					else
					{
						if (len == 0)
							server.DropClient(this);
						break;
					}
				}
				catch (SocketException e)
				{
					// This should no longer be needed with the socket.Poll call above.
					if (e.SocketErrorCode == SocketError.WouldBlock) break;

					server.DropClient(this);
					Log.Write("server", "Dropping client {0} because reading the data failed: {1}", PlayerIndex, e);
					return false;
				}
			}

			lastReceivedTime = Game.RunTime;
			TimeoutMessageShown = false;

			return true;
		}

		public void ReadData(Server server)
		{
			if (ReadDataInner(server))
			{
				while (Data.Count >= expectLength)
				{
					var bytes = PopBytes(expectLength);
					switch (state)
					{
						case ReceiveState.Header:
							{
								expectLength = BitConverter.ToInt32(bytes, 0) - 4;
								frame = BitConverter.ToInt32(bytes, 4);
								state = ReceiveState.Data;

								if (expectLength < 0 || expectLength > MaxOrderLength)
								{
									server.DropClient(this);
									Log.Write("server", "Dropping client {0} for excessive order length = {1}", PlayerIndex, expectLength);
									return;
								}

								break;
							}

						case ReceiveState.Data:
							{
								if (MostRecentFrame < frame)
									MostRecentFrame = frame;

								server.DispatchOrders(this, frame, bytes);
								expectLength = 8;
								state = ReceiveState.Header;

								break;
							}
					}
				}
			}
		}

		public void Dispose()
		{
			// If the send thread is still alive we must allow that to finish sending any data before disposing the connection.
			// If the send thread is not alive we must dispose it here because the send thread can't.
			if (sendThread.IsAlive)
				sendCancellationToken.Cancel();
			else
				Socket.Dispose();
		}
	}

	public enum ReceiveState { Header, Data }
}
