using System;
using System.Collections;
using System.Collections.Generic;

namespace PowerShell.MCP.Cmdlets
{
    /// <summary>
    /// 固定容量のローテートバッファ。古い要素は自動的に上書きされる。
    /// </summary>
    /// <typeparam name="T">要素の型</typeparam>
    public class RotateBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private int _head;      // 次に書き込む位置
        private int _count;     // 現在の要素数

        /// <summary>
        /// 指定した容量でローテートバッファを作成する。
        /// </summary>
        /// <param name="capacity">バッファの容量（1以上）</param>
        /// <exception cref="ArgumentOutOfRangeException">容量が1未満の場合</exception>
        public RotateBuffer(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
            
            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// バッファの容量を取得する。
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// 現在の要素数を取得する。
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// バッファが満杯かどうかを取得する。
        /// </summary>
        public bool IsFull => _count == _buffer.Length;

        /// <summary>
        /// 要素を追加する。バッファが満杯の場合、最も古い要素が上書きされる。
        /// </summary>
        /// <param name="item">追加する要素</param>
        public void Add(T item)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }

        /// <summary>
        /// 指定したインデックスの要素を取得する。
        /// 0 = 最も古い要素、Count - 1 = 最も新しい要素。
        /// </summary>
        /// <param name="index">インデックス（0 から Count - 1）</param>
        /// <returns>指定した位置の要素</returns>
        /// <exception cref="ArgumentOutOfRangeException">インデックスが範囲外の場合</exception>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 0 and {_count - 1}.");
                
                // 最も古い要素の位置を計算
                int start = (_head - _count + _buffer.Length) % _buffer.Length;
                return _buffer[(start + index) % _buffer.Length];
            }
        }

        /// <summary>
        /// 最も新しい要素を取得する。
        /// </summary>
        /// <exception cref="InvalidOperationException">バッファが空の場合</exception>
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
        /// 最も古い要素を取得する。
        /// </summary>
        /// <exception cref="InvalidOperationException">バッファが空の場合</exception>
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
        /// 末尾から指定した位置の要素を取得する。
        /// 0 = 最も新しい要素、Count - 1 = 最も古い要素。
        /// </summary>
        /// <param name="indexFromEnd">末尾からのインデックス（0 から Count - 1）</param>
        /// <returns>指定した位置の要素</returns>
        /// <exception cref="ArgumentOutOfRangeException">インデックスが範囲外の場合</exception>
        public T FromEnd(int indexFromEnd)
        {
            if (indexFromEnd < 0 || indexFromEnd >= _count)
                throw new ArgumentOutOfRangeException(nameof(indexFromEnd), $"Index must be between 0 and {_count - 1}.");
            
            return this[_count - 1 - indexFromEnd];
        }

        /// <summary>
        /// バッファをクリアする。
        /// </summary>
        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// バッファの内容を配列として取得する（古い順）。
        /// </summary>
        /// <returns>要素の配列</returns>
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
        /// 要素を古い順に列挙する。
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
