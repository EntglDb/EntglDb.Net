using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EntglDb.Demo.Game.MonoGame.UI;

/// <summary>
/// Simple single-line text input widget (printable ASCII, Backspace, Enter).
/// </summary>
public sealed class TextInputWidget
{
    private readonly int _maxLength;
    private string _text = string.Empty;
    private KeyboardState _prevKeys;
    private double _blinkTimer;
    private bool _cursorVisible = true;

    public string Text => _text;

    /// <summary>Fired when the user presses Enter with non-empty text.</summary>
    public event Action<string>? Confirmed;

    public TextInputWidget(int maxLength = 24)
    {
        _maxLength = maxLength;
        _prevKeys = Keyboard.GetState();
    }

    public void Update(GameTime gameTime)
    {
        // Cursor blink
        _blinkTimer += gameTime.ElapsedGameTime.TotalSeconds;
        if (_blinkTimer >= 0.5)
        {
            _blinkTimer = 0;
            _cursorVisible = !_cursorVisible;
        }

        var keys = Keyboard.GetState();

        if (WasPressed(keys, Keys.Back) && _text.Length > 0)
            _text = _text[..^1];
        else if (WasPressed(keys, Keys.Enter) && _text.Length > 0)
            Confirmed?.Invoke(_text);
        else
        {
            // Append printable keys
            foreach (var k in keys.GetPressedKeys())
            {
                if (_prevKeys.IsKeyDown(k)) continue;
                if (_text.Length >= _maxLength) break;

                char? ch = KeyToChar(k, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
                if (ch.HasValue)
                    _text += ch.Value;
            }
        }

        _prevKeys = keys;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Vector2 position, Color color)
    {
        var display = _text + (_cursorVisible ? "|" : " ");
        sb.DrawString(font, display, position, color);
    }

    private bool WasPressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && _prevKeys.IsKeyUp(key);

    private static char? KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9 && !shift)
            return (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            _ => null,
        };
    }
}
