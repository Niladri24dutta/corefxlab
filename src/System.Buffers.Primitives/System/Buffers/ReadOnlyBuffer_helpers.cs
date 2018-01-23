// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Sequences;
using System.Runtime.CompilerServices;

namespace System.Buffers
{
    public readonly partial struct ReadOnlyBuffer<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryGetBuffer(Position start, Position end, out ReadOnlyMemory<T> data, out Position next)
        {
            var startIndex = start.Index;
            var endIndex = end.Index;
            var type = GetType(startIndex, endIndex);

            startIndex = GetIndex(startIndex);
            endIndex = GetIndex(endIndex);

            switch (type)
            {
                case BufferType.MemoryList:
                    var bufferSegment = (IBufferList<T>) start.Segment;
                    var currentEndIndex = bufferSegment.Memory.Length;

                    if (bufferSegment == end.Segment)
                    {
                        currentEndIndex = endIndex;
                        next = default;
                    }
                    else
                    {
                        var nextSegment = bufferSegment.Next;
                        if (nextSegment == null)
                        {
                            if (end.Segment != null)
                            {
                                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.EndCursorNotReached);
                            }

                            next = default;
                        }
                        else
                        {
                            next = new Position(nextSegment, 0);
                        }
                    }

                    data = bufferSegment.Memory.Slice(startIndex, currentEndIndex - startIndex);

                    return true;


                case BufferType.OwnedMemory:
                    var ownedMemory = (OwnedMemory<T>) start.Segment;
                    data = ownedMemory.Memory.Slice(startIndex, endIndex - startIndex);

                    if (ownedMemory != end.Segment)
                    {
                        ThrowHelper.ThrowInvalidOperationException(ExceptionResource.EndCursorNotReached);
                    }

                    next = default;
                    return true;

                case BufferType.Array:
                    var array = (T[]) start.Segment;
                    data = new Memory<T>(array, startIndex, endIndex - startIndex);

                    if (array != end.Segment)
                    {
                        ThrowHelper.ThrowInvalidOperationException(ExceptionResource.EndCursorNotReached);
                    }

                    next = default;
                    return true;
            }

            ThrowHelper.ThrowNotSupportedException();
            next = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Position Seek(Position start, Position end, int bytes, bool checkEndReachable = true)
        {
            var startIndex = start.Index;
            var endIndex = end.Index;
            var type = GetType(startIndex, endIndex);

            startIndex = GetIndex(startIndex);
            endIndex = GetIndex(endIndex);

            if (start.Segment == end.Segment && endIndex - startIndex >= bytes)
            {
                return new Position(start.Segment, startIndex + bytes);
            }

            if (type == BufferType.MemoryList)
            {
                return SeekMultiSegment((IBufferList<byte>) start.Segment, startIndex, (IBufferList<byte>) end.Segment, endIndex, bytes, checkEndReachable);
            }

            ThrowHelper.ThrowInvalidOperationException(ExceptionResource.EndCursorNotReached);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Position Seek(Position start, Position end, long bytes, bool checkEndReachable = true)
        {
            var startIndex = start.Index;
            var endIndex = end.Index;
            var type = GetType(startIndex, endIndex);

            startIndex = GetIndex(startIndex);
            endIndex = GetIndex(endIndex);

            if (start.Segment == end.Segment && endIndex - startIndex >= bytes)
            {
                // end.Index >= bytes + Index and end.Index is int
                return new Position(start.Segment, startIndex + (int)bytes);
            }

            if (type == BufferType.MemoryList)
            {
                return SeekMultiSegment((IBufferList<byte>) start.Segment, startIndex, (IBufferList<byte>) end.Segment, endIndex, bytes, checkEndReachable);
            }

            ThrowHelper.ThrowInvalidOperationException(ExceptionResource.EndCursorNotReached);
            return default;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Position SeekMultiSegment(IBufferList<byte> start, int startIndex, IBufferList<byte> end, int endPosition, long bytes, bool checkEndReachable)
        {
            Position result = default;
            var foundResult = false;
            var current = start;
            var currentIndex = startIndex;

            while (current != null)
            {
                // We need to loop up until the end to make sure start and end are connected
                // if end is not trusted
                if (!foundResult)
                {
                    var memory = current.Memory;
                    var currentEnd = current == end ? endPosition : memory.Length;

                    memory = memory.Slice(0, currentEnd - currentIndex);
                    // We would prefer to put cursor in the beginning of next segment
                    // then past the end of previous one, but only if next exists

                    if (memory.Length > bytes ||
                       (memory.Length == bytes && current == null))
                    {
                        result = new Position(current, currentIndex + (int)bytes);
                        foundResult = true;
                        if (!checkEndReachable)
                        {
                            break;
                        }
                    }

                    bytes -= memory.Length;
                }

                current = current.Next;
                currentIndex = 0;
            }

            if (!foundResult)
            {
                ThrowHelper.ThrowCursorOutOfBoundsException();
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetLength(Position start, Position end)
        {
            var startIndex = start.Index;
            var endIndex = end.Index;
            var type = GetType(startIndex, endIndex);

            startIndex = GetIndex(startIndex);
            endIndex = GetIndex(endIndex);

            switch (type)
            {
                case BufferType.MemoryList:
                    return GetLength((IBufferList<T>) start.Segment, startIndex, (IBufferList<T>)end.Segment, endIndex);

                case BufferType.OwnedMemory:
                case BufferType.Array:
                    return endIndex - startIndex;
            }

            ThrowHelper.ThrowInvalidOperationException(ExceptionResource.UnexpectedSegmentType);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetLength(
            IBufferList<T> start,
            int startIndex,
            IBufferList<T> endSegment,
            int endIndex)
        {
            if (start == endSegment)
            {
                return endIndex - startIndex;
            }

            return (endSegment.RunningLength - start.Next.RunningLength)
                   + (start.Memory.Length - startIndex)
                   + endIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BoundsCheck(Position end, Position newCursor)
        {
            switch (end.Segment)
            {
                case byte[] _:
                case OwnedMemory<byte> _:
                    if (newCursor.Index > end.Index)
                    {
                        ThrowHelper.ThrowCursorOutOfBoundsException();
                    }
                    return;
                case IBufferList<T> memoryList:
                    var segment = (IBufferList<T>)newCursor.Segment;
                    if (segment.RunningLength - end.Index > memoryList.RunningLength - newCursor.Index)
                    {
                        ThrowHelper.ThrowCursorOutOfBoundsException();
                    }
                    return;
                default:
                    ThrowHelper.ThrowCursorOutOfBoundsException();
                    return;
            }
        }

        private class ReadOnlyBufferSegment : IBufferList<T>
        {
            public Memory<T> Memory { get; set; }
            public IBufferList<T> Next { get; set; }
            public long RunningLength { get; set; }
        }
    }
}
