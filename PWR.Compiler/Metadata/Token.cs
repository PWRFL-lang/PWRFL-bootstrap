internal readonly struct Token(byte type, int value)
{
	private readonly int _value = unchecked(value | (type << 24));

	public int Type => (int)((uint)_value & 0xFF000000) >> 24;
	public int Value => _value & 0x00FFFFFF;

	public int AsInt => _value;

	public bool IsNull => _value == 0;
}
