using System;
using System.Collections;
using System.Collections.Generic;

namespace PowerShell.MCP.Cmdlets
{
    /// <summary>
    /// Fixed-capacity rotating buffer. Old elements are automatically overwritten.
    /// </summary>
    /// <typeparam name="T">Element type</typeparam>
    public class RotateBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private int _head;      // Next write position
        private int _count;     // Current element count

        /// <summary>
        /// Creates a rotating buffer with specified capacity.
        /// </summary>
        /// <param name="capacity">Buffer capacity (1 or more)</param>
        /// <exception cref="ArgumentOutOfRangeException">If capacity is less than 1</exception>
        public RotateBuffer(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
            
            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Gets the buffer capacity.
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// Gets the current element count.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets whether the buffer is full.
        /// </summary>
        public bool IsFull => _count == _buffer.Length;

        /// <summary>
        /// Adds an element. When buffer is full, oldest element is overwritten.
        /// </summary>
        /// <param name="item">Element to add</param>
        public void Add(T item)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }

        /// <summary>
        /// Gets element at specified index.
        /// 0 = oldest element, Count - 1 = newest element.
        /// </summary>
        /// <param name="index">Index (0 to Count - 1)</param>
        /// <returns>Element at specified position</returns>
        /// <exception cref="ArgumentOutOfRangeException">If index is out of range</exception>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 0 and {_count - 1}.");
                
                // Calculate position of oldest element
                int start = (_head - _count + _buffer.Length) % _buffer.Length;
                return _buffer[(start + index) % _buffer.Length];
            }
        }

        /// <summary>
        /// Gets the newest element.
        /// </summary>
        /// <exception cref="InvalidOperationException">If buffer is empty</exception>
        public T Newest
        {
            get
            {
                if (_count == 0)
                    throw new InvalidOperationException("Buffer is empty.");
                return this[_count - 1];
            }
        }

        /// <summary>
        /// Gets the oldest element.
        /// </summary>
        /// <exception cref="InvalidOperationException">If buffer is empty</exception>
        public T Oldest
        {
            get
            {
                if (_count == 0)
                    throw new InvalidOperationException("Buffer is empty.");
                return this[0];
            }
        }

        /// <summary>
        /// Gets element at specified position from end.
        /// 0 = newest element, Count - 1 = oldest element.
        /// </summary>
        /// <param name="indexFromEnd">Index from end (0 to Count - 1)</param>
        /// <returns>Element at specified position</returns>
        /// <exception cref="ArgumentOutOfRangeException">If index is out of range</exception>
        public T FromEnd(int indexFromEnd)
        {
            if (indexFromEnd < 0 || indexFromEnd >= _count)
                throw new ArgumentOutOfRangeException(nameof(indexFromEnd), $"Index must be between 0 and {_count - 1}.");
            
            return this[_count - 1 - indexFromEnd];
        }

        /// <summary>
        /// Clears the buffer.
        /// </summary>
        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Gets buffer contents as array (oldest first).
        /// </summary>
        /// <returns>Array of elements</returns>
        public T[] ToArray()
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                result[i] = this[i];
            }
            return result;
        }

        /// <summary>
        /// Enumerates elements from oldest to newest.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
