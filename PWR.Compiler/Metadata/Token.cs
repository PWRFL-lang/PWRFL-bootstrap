internal readonly struct Token(byte type, int value)
{
	private readonly uint _value = (uint)(value | (type << 24));

	public uint Type => (_value & 0xFF000000) >> 24;
	public uint Value => _value & 0x00FFFFFF;

	public uint AsUInt => _value;

	public bool IsNull => _value == 0;
}
