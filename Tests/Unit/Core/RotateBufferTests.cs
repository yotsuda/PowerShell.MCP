using Xunit;
using PowerShell.MCP.Cmdlets;
using System;
using System.Linq;

namespace PowerShell.MCP.Tests.Unit.Core
{
    public class RotateBufferTests
    {
        [Fact]
        public void Constructor_WithValidCapacity_CreatesBuffer()
        {
            var buffer = new RotateBuffer<int>(5);
            Assert.Equal(5, buffer.Capacity);
            Assert.Equal(0, buffer.Count);
            Assert.False(buffer.IsFull);
        }

        [Fact]
        public void Constructor_WithZeroCapacity_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RotateBuffer<int>(0));
        }

        [Fact]
        public void Constructor_WithNegativeCapacity_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RotateBuffer<int>(-1));
        }

        [Fact]
        public void Add_SingleItem_IncreasesCount()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            Assert.Equal(1, buffer.Count);
            Assert.Equal(1, buffer[0]);
        }

        [Fact]
        public void Add_MultipleItems_MaintainsOrder()
        {
            var buffer = new RotateBuffer<int>(5);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            Assert.Equal(3, buffer.Count);
            Assert.Equal(1, buffer[0]); // oldest
            Assert.Equal(2, buffer[1]);
            Assert.Equal(3, buffer[2]); // newest
        }

        [Fact]
        public void Add_WhenFull_OverwritesOldest()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4); // overwrites 1

            Assert.Equal(3, buffer.Count);
            Assert.True(buffer.IsFull);
            Assert.Equal(2, buffer[0]); // oldest now
            Assert.Equal(3, buffer[1]);
            Assert.Equal(4, buffer[2]); // newest
        }

        [Fact]
        public void Add_MultipleCycles_MaintainsCorrectOrder()
        {
            var buffer = new RotateBuffer<int>(3);
            for (int i = 1; i <= 10; i++)
            {
                buffer.Add(i);
            }

            Assert.Equal(3, buffer.Count);
            Assert.Equal(8, buffer[0]);  // oldest
            Assert.Equal(9, buffer[1]);
            Assert.Equal(10, buffer[2]); // newest
        }

        [Fact]
        public void Indexer_WithValidIndex_ReturnsCorrectElement()
        {
            var buffer = new RotateBuffer<string>(3);
            buffer.Add("a");
            buffer.Add("b");
            buffer.Add("c");

            Assert.Equal("a", buffer[0]);
            Assert.Equal("b", buffer[1]);
            Assert.Equal("c", buffer[2]);
        }

        [Fact]
        public void Indexer_WithInvalidIndex_ThrowsException()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer[-1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer[2]);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer[10]);
        }

        [Fact]
        public void Newest_ReturnsLastAddedElement()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            Assert.Equal(3, buffer.Newest);

            buffer.Add(4);
            Assert.Equal(4, buffer.Newest);
        }

        [Fact]
        public void Newest_WhenEmpty_ThrowsException()
        {
            var buffer = new RotateBuffer<int>(3);
            Assert.Throws<InvalidOperationException>(() => buffer.Newest);
        }

        [Fact]
        public void Oldest_ReturnsFirstElement()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            Assert.Equal(1, buffer.Oldest);

            buffer.Add(4);
            Assert.Equal(2, buffer.Oldest);
        }

        [Fact]
        public void Oldest_WhenEmpty_ThrowsException()
        {
            var buffer = new RotateBuffer<int>(3);
            Assert.Throws<InvalidOperationException>(() => buffer.Oldest);
        }

        [Fact]
        public void FromEnd_ReturnsCorrectElement()
        {
            var buffer = new RotateBuffer<int>(5);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);
            buffer.Add(5);

            Assert.Equal(5, buffer.FromEnd(0)); // newest
            Assert.Equal(4, buffer.FromEnd(1));
            Assert.Equal(3, buffer.FromEnd(2));
            Assert.Equal(2, buffer.FromEnd(3));
            Assert.Equal(1, buffer.FromEnd(4)); // oldest
        }

        [Fact]
        public void FromEnd_WithInvalidIndex_ThrowsException()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.FromEnd(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.FromEnd(2));
        }

        [Fact]
        public void Clear_ResetsBuffer()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            buffer.Clear();

            Assert.Equal(0, buffer.Count);
            Assert.False(buffer.IsFull);
        }

        [Fact]
        public void ToArray_ReturnsElementsInOrder()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);

            var array = buffer.ToArray();

            Assert.Equal(new[] { 2, 3, 4 }, array);
        }

        [Fact]
        public void GetEnumerator_EnumeratesInOrder()
        {
            var buffer = new RotateBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);

            var list = buffer.ToList();

            Assert.Equal(new[] { 2, 3, 4 }, list);
        }

        [Fact]
        public void Capacity_One_WorksCorrectly()
        {
            var buffer = new RotateBuffer<int>(1);

            buffer.Add(1);
            Assert.Equal(1, buffer.Count);
            Assert.Equal(1, buffer[0]);
            Assert.Equal(1, buffer.Newest);
            Assert.Equal(1, buffer.Oldest);

            buffer.Add(2);
            Assert.Equal(1, buffer.Count);
            Assert.Equal(2, buffer[0]);
            Assert.Equal(2, buffer.Newest);
            Assert.Equal(2, buffer.Oldest);
        }

        [Fact]
        public void StringBuffer_WorksCorrectly()
        {
            var buffer = new RotateBuffer<string>(3);
            buffer.Add("line 1");
            buffer.Add("line 2");
            buffer.Add("line 3");
            buffer.Add("line 4");

            Assert.Equal("line 2", buffer.Oldest);
            Assert.Equal("line 4", buffer.Newest);
            Assert.Equal(new[] { "line 2", "line 3", "line 4" }, buffer.ToArray());
        }
    }
}
