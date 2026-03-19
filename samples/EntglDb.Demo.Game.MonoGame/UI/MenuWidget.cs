using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EntglDb.Demo.Game.MonoGame.UI;

/// <summary>
/// Simple keyboard-navigable vertical menu.
/// </summary>
public sealed class MenuWidget
{
    private readonly string[] _items;
    private int _selectedIndex;

    private KeyboardState _prevKeys;

    public int SelectedIndex => _selectedIndex;
    public string SelectedItem => _items[_selectedIndex];

    /// <summary>Fired when the user confirms a selection (Enter / Space).</summary>
    public event Action<int>? ItemConfirmed;

    public MenuWidget(IEnumerable<string> items)
    {
        _items = items.ToArray();
        _prevKeys = Keyboard.GetState();
    }

    public void Update()
    {
        var keys = Keyboard.GetState();

        if (WasPressed(keys, Keys.Down))
            _selectedIndex = (_selectedIndex + 1) % _items.Length;
        else if (WasPressed(keys, Keys.Up))
            _selectedIndex = (_selectedIndex - 1 + _items.Length) % _items.Length;
        else if (WasPressed(keys, Keys.Enter) || WasPressed(keys, Keys.Space))
            ItemConfirmed?.Invoke(_selectedIndex);

        _prevKeys = keys;
    }

    private bool WasPressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && _prevKeys.IsKeyUp(key);

    public void Draw(
        SpriteBatch sb,
        SpriteFont font,
        Vector2 origin,
        Color normalColor,
        Color highlightColor,
        float lineHeight = 0)
    {
        if (lineHeight <= 0)
            lineHeight = font.MeasureString("A").Y + 8;

        for (int i = 0; i < _items.Length; i++)
        {
            var color = i == _selectedIndex ? highlightColor : normalColor;
            var prefix = i == _selectedIndex ? "> " : "  ";
            sb.DrawString(font, prefix + ClassSymbol.Sanitize(_items[i]),
                origin + new Vector2(0, i * lineHeight), color);
        }
    }
}
