/*******************************************************************************
 * Copyright 2012-2014 by Aerospike.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 ******************************************************************************/
using System;
using System.Net.Sockets;
using System.Threading;

namespace Aerospike.Client
{
	public abstract class SyncCommand : Command
	{
		public void Execute()
		{
			Policy policy = GetPolicy();
			int remainingMillis = policy.timeout;
			DateTime limit = DateTime.UtcNow.AddMilliseconds(remainingMillis);
			int failedNodes = 0;
			int failedConns = 0;
			int iterations = 0;

			dataBuffer = ThreadLocalData.GetBuffer();

			// Execute command until successful, timed out or maximum iterations have been reached.
			while (true)
			{
				Node node = null;
				try
				{
					node = GetNode();
					Connection conn = node.GetConnection(remainingMillis);

					try
					{
						// Set command buffer.
						WriteBuffer();

						// Reset timeout in send buffer (destined for server) and socket.
						ByteUtil.IntToBytes((uint)remainingMillis, dataBuffer, 22);

						// Send command.
						conn.Write(dataBuffer, dataOffset);

						// Parse results.
						ParseResult(conn);

						// Reflect healthy status.
						conn.UpdateLastUsed();
						node.RestoreHealth();

						// Put connection back in pool.
						node.PutConnection(conn);

						// Command has completed successfully.  Exit method.
						return;
					}
					catch (AerospikeException ae)
					{
						// Close socket to flush out possible garbage.  Do not put back in pool.
						conn.Close();
						throw ae;
					}
					catch (SocketException ioe)
					{
						// IO errors are considered temporary anomalies.  Retry.
						// Close socket to flush out possible garbage.  Do not put back in pool.
						conn.Close();

						if (Log.DebugEnabled())
						{
							Log.Debug("Node " + node + ": " + Util.GetErrorMessage(ioe));
						}
						// IO error means connection to server node is unhealthy.
						// Reflect this status.
						node.DecreaseHealth();
					}
					catch (Exception)
					{
						// All runtime exceptions are considered fatal.  Do not retry.
						// Close socket to flush out possible garbage.  Do not put back in pool.
						conn.Close();
						throw;
					}
				}
				catch (AerospikeException.InvalidNode)
				{
					// Node is currently inactive.  Retry.
					failedNodes++;
				}
				catch (AerospikeException.Connection ce)
				{
					// Socket connection error has occurred. Decrease health and retry.
					node.DecreaseHealth();

					if (Log.DebugEnabled())
					{
						Log.Debug("Node " + node + ": " + Util.GetErrorMessage(ce));
					}
					failedConns++;
				}

				if (++iterations > policy.maxRetries)
				{
					break;
				}

				// Check for client timeout.
				if (policy.timeout > 0)
				{
					remainingMillis = (int)limit.Subtract(DateTime.UtcNow).TotalMilliseconds - policy.sleepBetweenRetries;

					if (remainingMillis <= 0)
					{
						break;
					}
				}

				if (policy.sleepBetweenRetries > 0)
				{
					// Sleep before trying again.
					Util.Sleep(policy.sleepBetweenRetries);
				}
			}

			throw new AerospikeException.Timeout(policy.timeout, iterations, failedNodes, failedConns);
		}

		protected internal sealed override void SizeBuffer()
		{
			if (dataOffset > dataBuffer.Length)
			{
				dataBuffer = ThreadLocalData.ResizeBuffer(dataOffset);
			}
		}

		protected internal void SizeBuffer(int size)
		{
			if (size > dataBuffer.Length)
			{
				dataBuffer = ThreadLocalData.ResizeBuffer(size);
			}
		}

		protected internal abstract Node GetNode();
		protected internal abstract void ParseResult(Connection conn);
	}
}
