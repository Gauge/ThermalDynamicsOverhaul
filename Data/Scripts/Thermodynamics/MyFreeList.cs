using System;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics
{
	public class MyFreeList<TItem>
	{
		public TItem[] list;

		private int m_size;

		private readonly Queue<int> m_freePositions;

		private readonly TItem m_default;

		/// <summary>
		/// Length of the internal buffer occupied by a mix of free and empty nodes, data past this length is all free.
		/// </summary>
		public int UsedLength => m_size;

		/// <summary>
		/// Total number of allocated positions.
		/// </summary>
		public int Count => m_size - m_freePositions.Count;

		public int Capacity => list.Length;

		public MyFreeList(int capacity = 16, TItem defaultValue = default(TItem))
		{
			list = new TItem[16];
			m_freePositions = new Queue<int>(capacity / 2);
			m_default = defaultValue;
		}

		public int Allocate()
		{
			if (m_freePositions.Count > 0)
			{
				return m_freePositions.Dequeue();
			}
			if (m_size == list.Length)
			{
				Array.Resize(ref list, list.Length << 1);
			}
			return m_size++;
		}

		public int Allocate(TItem value)
		{
			int num = Allocate();
			list[num] = value;
			return num;
		}

		public void Free(int position)
		{
			list[position] = m_default;
			if (position == m_size)
			{
				m_size--;
			}
			else
			{
				m_freePositions.Enqueue(position);
			}
		}

		public TItem[] GetInternalArray()
		{
			return list;
		}

		public bool KeyValid(int key)
		{
			return (uint)key < m_size;
		}

		public void Clear()
		{
			for (int i = 0; i < m_size; i++)
			{
				list[i] = default(TItem);
			}
			m_size = 0;
			m_freePositions.Clear();
		}
	}

}
