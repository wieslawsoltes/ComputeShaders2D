using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace ComputeShaders2D.Core.Geometry;

internal static class SvgPathParser
{
    public static PathBuilder Parse(string data)
    {
        var builder = new PathBuilder();
        if (string.IsNullOrWhiteSpace(data))
            return builder;

        var reader = new PathDataReader(data.AsSpan());
        var current = Vector2.Zero;
        var start = Vector2.Zero;
        var lastControl = Vector2.Zero;
        var command = 'M';

        while (reader.TryReadCommand(ref command))
        {
            switch (command)
            {
                case 'M':
                case 'm':
                    current = ReadPoint(ref reader, command == 'm', current);
                    builder.MoveTo(current.X, current.Y);
                    start = current;
                    // Subsequent pairs are treated as line commands.
                    while (reader.TryPeekNumber())
                    {
                        current = ReadPoint(ref reader, command == 'm', current);
                        builder.LineTo(current.X, current.Y);
                    }
                    command = command == 'm' ? 'l' : 'L';
                    break;

                case 'L':
                case 'l':
                    while (reader.TryPeekNumber())
                    {
                        current = ReadPoint(ref reader, command == 'l', current);
                        builder.LineTo(current.X, current.Y);
                    }
                    break;

                case 'H':
                case 'h':
                    while (reader.TryReadFloat(out var x))
                    {
                        current = command == 'h'
                            ? new Vector2(current.X + x, current.Y)
                            : new Vector2(x, current.Y);
                        builder.LineTo(current.X, current.Y);
                    }
                    break;

                case 'V':
                case 'v':
                    while (reader.TryReadFloat(out var y))
                    {
                        current = command == 'v'
                            ? new Vector2(current.X, current.Y + y)
                            : new Vector2(current.X, y);
                        builder.LineTo(current.X, current.Y);
                    }
                    break;

                case 'C':
                case 'c':
                    while (reader.TryPeekNumber())
                    {
                        var c1 = ReadPoint(ref reader, command == 'c', current);
                        var c2 = ReadPoint(ref reader, command == 'c', current);
                        var end = ReadPoint(ref reader, command == 'c', current);
                        builder.BezierTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                        lastControl = c2;
                        current = end;
                    }
                    break;

                case 'S':
                case 's':
                    while (reader.TryPeekNumber())
                    {
                        var c1 = Reflect(current, lastControl);
                        var c2 = ReadPoint(ref reader, command == 's', current);
                        var end = ReadPoint(ref reader, command == 's', current);
                        builder.BezierTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                        lastControl = c2;
                        current = end;
                    }
                    break;

                case 'Q':
                case 'q':
                    while (reader.TryPeekNumber())
                    {
                        var control = ReadPoint(ref reader, command == 'q', current);
                        var end = ReadPoint(ref reader, command == 'q', current);
                        builder.QuadTo(control.X, control.Y, end.X, end.Y);
                        lastControl = control;
                        current = end;
                    }
                    break;

                case 'T':
                case 't':
                    while (reader.TryPeekNumber())
                    {
                        var control = Reflect(current, lastControl);
                        var end = ReadPoint(ref reader, command == 't', current);
                        builder.QuadTo(control.X, control.Y, end.X, end.Y);
                        lastControl = control;
                        current = end;
                    }
                    break;

                case 'A':
                case 'a':
                    while (reader.TryPeekNumber())
                    {
                        var rx = reader.ReadFloatOrThrow();
                        var ry = reader.ReadFloatOrThrow();
                        var rotation = reader.ReadFloatOrThrow();
                        var largeArc = reader.ReadFlagOrThrow();
                        var sweep = reader.ReadFlagOrThrow();
                        var target = ReadPoint(ref reader, command == 'a', current);

                        foreach (var segment in SvgArcHelper.ToCubic(current, target, rx, ry, rotation, largeArc, sweep))
                        {
                            builder.BezierTo(segment.C1.X, segment.C1.Y, segment.C2.X, segment.C2.Y, segment.P2.X, segment.P2.Y);
                        }

                        current = target;
                        lastControl = current;
                    }
                    break;

                case 'Z':
                case 'z':
                    builder.ClosePath();
                    current = start;
                    break;

                default:
                    // Skip unsupported commands (e.g., arcs) gracefully.
                    reader.SkipUnsupported();
                    break;
            }
        }

        return builder;
    }

    private static Vector2 ReadPoint(ref PathDataReader reader, bool relative, Vector2 reference)
    {
        var x = reader.ReadFloatOrThrow();
        var y = reader.ReadFloatOrThrow();
        var point = new Vector2(x, y);
        if (relative)
        {
            point += reference;
        }
        return point;
    }

    private static Vector2 Reflect(Vector2 current, Vector2 control)
        => current * 2 - control;

    private ref struct PathDataReader
    {
        private readonly ReadOnlySpan<char> _data;
        private int _index;
        private char _currentCommand;

        public PathDataReader(ReadOnlySpan<char> data)
        {
            _data = data;
            _index = 0;
            _currentCommand = '\0';
        }

        public bool TryReadCommand(ref char command)
        {
            SkipWhitespace();
            if (_index >= _data.Length)
                return false;

            var c = _data[_index];
            if (char.IsLetter(c))
            {
                command = c;
                _currentCommand = c;
                _index++;
                return true;
            }

            if (_currentCommand == '\0')
                return false;

            command = _currentCommand;
            return true;
        }

        public bool TryReadFloat(out float value)
        {
            SkipWhitespace();
            if (_index >= _data.Length)
            {
                value = 0;
                return false;
            }

            var start = _index;
            var hasNumber = false;
            while (_index < _data.Length)
            {
                var ch = _data[_index];
                if (char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.' || ch == 'e' || ch == 'E')
                {
                    _index++;
                    hasNumber = true;
                }
                else if (ch == ',' || char.IsWhiteSpace(ch))
                {
                    break;
                }
                else
                {
                    break;
                }
            }

            if (!hasNumber)
            {
                value = 0;
                return false;
            }

            var span = _data.Slice(start, _index - start);
            value = float.Parse(span, NumberStyles.Float, CultureInfo.InvariantCulture);
            SkipSeparators();
            return true;
        }

        public float ReadFloatOrThrow()
        {
            if (!TryReadFloat(out var value))
                throw new FormatException("Unexpected end of path data.");
            return value;
        }

        public bool ReadFlagOrThrow()
        {
            if (!TryReadFloat(out var value))
                throw new FormatException("Unexpected flag in path data.");
            return Math.Abs(value) > 0.5f;
        }

        public bool TryPeekNumber()
        {
            var idx = _index;
            while (idx < _data.Length && char.IsWhiteSpace(_data[idx]))
                idx++;
            if (idx >= _data.Length)
                return false;
            var ch = _data[idx];
            return char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.';
        }

        public void SkipUnsupported()
        {
            while (_index < _data.Length && !char.IsLetter(_data[_index]))
                _index++;
        }

        private void SkipWhitespace()
        {
            while (_index < _data.Length && char.IsWhiteSpace(_data[_index]))
                _index++;
            SkipSeparators();
        }

        private void SkipSeparators()
        {
            while (_index < _data.Length && (_data[_index] == ',' || char.IsWhiteSpace(_data[_index])))
                _index++;
        }
    }
}
