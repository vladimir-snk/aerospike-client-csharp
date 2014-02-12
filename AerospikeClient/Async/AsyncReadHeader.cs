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
using System.Collections.Generic;

namespace Aerospike.Client
{
	public sealed class AsyncReadHeader : AsyncSingleCommand
	{
		private readonly Policy policy;
		private readonly RecordListener listener;
		private Record record;

		public AsyncReadHeader(AsyncCluster cluster, Policy policy, RecordListener listener, Key key) 
			: base(cluster, key)
		{
			this.policy = (policy == null) ? new Policy() : policy;
			this.listener = listener;
		}

		protected internal override Policy GetPolicy()
		{
			return policy;
		}

		protected internal override void WriteBuffer()
		{
			SetReadHeader(key);
		}

		protected internal override void ParseResult()
		{
			int resultCode = dataBuffer[5];

			if (resultCode == 0)
			{
				int generation = ByteUtil.BytesToInt(dataBuffer, 6);
				int expiration = ByteUtil.BytesToInt(dataBuffer, 10);

				record = new Record(null, generation, expiration);
			}
			else
			{
				if (resultCode == ResultCode.KEY_NOT_FOUND_ERROR)
				{
					record = null;
				}
				else
				{
					throw new AerospikeException(resultCode);
				}
			}
		}

		protected internal override void OnSuccess()
		{
			if (listener != null)
			{
				listener.OnSuccess(key, record);
			}
		}

		protected internal override void OnFailure(AerospikeException e)
		{
			if (listener != null)
			{
				listener.OnFailure(e);
			}
		}
	}
}
