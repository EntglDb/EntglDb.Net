using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>
/// Base contract for all game screens.
/// Each screen owns its own Update/Draw cycle and requests transitions
/// by raising the <see cref="NavigateTo"/> event.
/// </summary>
public abstract class GameScreen
{
    /// <summary>
    /// Raised when the screen wants to transition to a different screen.
    /// The argument is the target screen instance, or <c>null</c> to pop.
    /// </summary>
    public event Action<GameScreen?>? NavigateTo;

    protected void GoTo(GameScreen? target) => NavigateTo?.Invoke(target);

    /// <summary>Called once when the screen becomes active.</summary>
    public virtual void Enter() { }

    /// <summary>Called once when the screen is deactivated.</summary>
    public virtual void Exit() { }

    public abstract void Update(GameTime gameTime);
    public abstract void Draw(SpriteBatch spriteBatch, GameTime gameTime);
}
