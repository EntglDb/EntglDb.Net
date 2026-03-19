using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>
/// Manages a stack of <see cref="GameScreen"/> objects and routes
/// Update / Draw to the topmost active screen.
/// </summary>
public sealed class ScreenManager
{
    private readonly Stack<GameScreen> _stack = new();

    public GameScreen? Current => _stack.Count > 0 ? _stack.Peek() : null;

    /// <summary>Pushes a new screen and wires up its navigation event.</summary>
    public void Push(GameScreen screen)
    {
        Current?.Exit();
        screen.NavigateTo += OnNavigateTo;
        _stack.Push(screen);
        screen.Enter();
    }

    private void OnNavigateTo(GameScreen? target)
    {
        // Pop current screen
        if (_stack.Count > 0)
        {
            var top = _stack.Pop();
            top.NavigateTo -= OnNavigateTo;
            top.Exit();
        }

        if (target != null)
            Push(target);
    }

    public void Update(GameTime gameTime) => Current?.Update(gameTime);

    public void Draw(SpriteBatch spriteBatch, GameTime gameTime) =>
        Current?.Draw(spriteBatch, gameTime);
}
