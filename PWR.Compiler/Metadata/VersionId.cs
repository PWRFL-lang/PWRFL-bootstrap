using System;
using System.Security.Cryptography;
using System.Text;

namespace PWR.Compiler.Metadata;

// The version ID of a library: a v5 GUID hash of the library's name and metadata.
//
// The metadata is everything another library can depend on: the types, their layout and the slots
// of their VMTs, the signatures, the values of constants, and the version IDs of the libraries it was
// built against. So two builds of a library share an ID exactly when nothing a program built against
// one can see has changed. A change to internal implementation details leaves the hash unchanged.
internal static class VersionId
{
	private static readonly Guid Namespace = new("5f0c7b2e-8a41-4d36-9b5e-2c61d0a7e913");

	public static Guid Compute(string libraryName, ReadOnlySpan<byte> header, ReadOnlySpan<byte> metadata, ReadOnlySpan<byte> blob)
	{
		using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
		Span<byte> ns = stackalloc byte[16];
		Namespace.TryWriteBytes(ns, bigEndian: true, out _);
		sha.AppendData(ns);
		Append(sha, Encoding.UTF8.GetBytes(libraryName));
		Append(sha, header);
		Append(sha, metadata);
		Append(sha, blob);
		var hash = sha.GetHashAndReset();
		// The version and variant bits of a name-based (SHA-1) GUID, per RFC 4122.
		hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
		hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
		return new Guid(hash.AsSpan(0, 16), bigEndian: true);
	}

	// Each part is length-prefixed, so that the boundary between two can't move without the hash changing.
	private static void Append(IncrementalHash sha, ReadOnlySpan<byte> part)
	{
		Span<byte> length = stackalloc byte[sizeof(int)];
		BitConverter.TryWriteBytes(length, part.Length);
		sha.AppendData(length);
		sha.AppendData(part);
	}
}
