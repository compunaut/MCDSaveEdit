using System;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;

namespace PakReader
{
    static class ReaderExtensions
    {
        public static string ReadFString(this BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count == 0) return null;
            bool unicode = count < 0;
            long characters = Math.Abs((long)count);
            long byteCount = characters * (unicode ? 2 : 1);
            if (byteCount > 16 * 1024 * 1024)
                throw new InvalidDataException("Archive string exceeds the supported limit.");
            byte[] data = reader.ReadBytesExact((int)byteCount);
            if (data[data.Length - 1] != 0 || (unicode && data[data.Length - 2] != 0))
                throw new InvalidDataException("Archive string is not null terminated.");
            return (unicode ? Encoding.Unicode : Encoding.UTF8).GetString(data, 0, data.Length - (unicode ? 2 : 1));
        }

        public static byte[] ReadBytesExact(this BinaryReader reader, int count)
        {
            ValidateRange(reader.BaseStream, reader.BaseStream.Position, count);
            byte[] bytes = new byte[count];
            reader.BaseStream.ReadExactly(bytes);
            return bytes;
        }

        internal static void ValidateRange(Stream stream, long offset, long size)
        {
            if (offset < 0 || size < 0 || size > 256 * 1024 * 1024 ||
                offset > stream.Length || size > stream.Length - offset)
                throw new InvalidDataException("Archive range is invalid or exceeds the supported limit.");
        }

        private sealed class AllocationBudget { internal long Remaining = 256L * 1024 * 1024; }
        private static readonly ConditionalWeakTable<Stream, AllocationBudget> AllocationBudgets = new();

        internal static int ValidateCount(this BinaryReader reader, long count, int minimumBytes = 1, int allocationBytes = 64)
        {
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (count < 0 || count > 1000000 || minimumBytes <= 0 || count > remaining / minimumBytes)
                throw new InvalidDataException("Archive collection count is invalid.");
            long allocation = checked(count * allocationBytes);
            reader.ReserveAllocation(allocation);
            return (int)count;
        }

        internal static void ReserveAllocation(this BinaryReader reader, long allocation)
        {
            var budget = AllocationBudgets.GetOrCreateValue(reader.BaseStream);
            if (allocation < 0 || allocation > budget.Remaining)
                throw new InvalidDataException("Archive collection allocation budget exceeded.");
            budget.Remaining -= allocation;
        }

        public static T[] ReadTArray<T>(this BinaryReader reader, Func<T> Getter)
        {
            int SerializeNum = reader.ValidateCount(reader.ReadInt32());
            T[] A = new T[SerializeNum];
            for(int i = 0; i < SerializeNum; i++)
            {
                A[i] = Getter();
            }
            return A;
        }
    }
}
