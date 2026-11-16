using System;
using System.Collections.Generic;
using System.Globalization;

namespace NeeView.Effects.Shaders
{
    /// <summary>
    /// Small arithmetic evaluator for MagpieFX WIDTH/HEIGHT expressions.
    /// Supports + - * / parentheses, unary signs and named constants.
    /// </summary>
    public sealed class MagpieExpression
    {
        private readonly string _text;
        private int _position;
        private IReadOnlyDictionary<string, double> _constants = new Dictionary<string, double>();

        private MagpieExpression(string text)
        {
            _text = text ?? "";
        }

        public static double Evaluate(string text, IReadOnlyDictionary<string, double> constants)
        {
            var expression = new MagpieExpression(text);
            expression._constants = constants;
            var value = expression.ParseExpression();
            expression.SkipWhitespace();
            if (expression._position != expression._text.Length)
            {
                throw new FormatException($"Invalid expression: {text}");
            }
            return value;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (_position >= _text.Length)
                {
                    return value;
                }
                var op = _text[_position];
                if (op != '+' && op != '-')
                {
                    return value;
                }
                _position++;
                var right = ParseTerm();
                value = op == '+' ? value + right : value - right;
            }
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (_position >= _text.Length)
                {
                    return value;
                }
                var op = _text[_position];
                if (op != '*' && op != '/')
                {
                    return value;
                }
                _position++;
                var right = ParseFactor();
                if (op == '/' && right == 0)
                {
                    throw new DivideByZeroException("Expression division by zero.");
                }
                value = op == '*' ? value * right : value / right;
            }
        }

        private double ParseFactor()
        {
            SkipWhitespace();
            if (_position >= _text.Length)
            {
                throw new FormatException("Unexpected end of expression.");
            }

            var c = _text[_position];
            if (c == '+' || c == '-')
            {
                _position++;
                var value = ParseFactor();
                return c == '-' ? -value : value;
            }
            if (c == '(')
            {
                _position++;
                var value = ParseExpression();
                SkipWhitespace();
                if (_position >= _text.Length || _text[_position] != ')')
                {
                    throw new FormatException("Missing closing parenthesis.");
                }
                _position++;
                return value;
            }
            if (char.IsDigit(c) || c == '.')
            {
                var start = _position;
                while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
                {
                    _position++;
                }
                var number = _text[start.._position];
                return double.Parse(number, CultureInfo.InvariantCulture);
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = _position;
                while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_'))
                {
                    _position++;
                }
                var name = _text[start.._position];
                if (_constants.TryGetValue(name, out var value))
                {
                    return value;
                }
                throw new FormatException($"Unknown expression constant: {name}");
            }
            throw new FormatException($"Unexpected character in expression: {c}");
        }
    }
}
