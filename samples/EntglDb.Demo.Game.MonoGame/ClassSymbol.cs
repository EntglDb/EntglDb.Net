namespace EntglDb.Demo.Game.MonoGame;

/// <summary>ASCII symbols for hero classes — SpriteFont-safe replacements for emoji.</summary>
internal static class ClassSymbol
{
    public static string For(HeroClass cls) => cls switch
    {
        HeroClass.Warrior     => "[W]",
        HeroClass.Mage        => "[M]",
        HeroClass.Rogue       => "[R]",
        HeroClass.Paladin     => "[P]",
        HeroClass.Ranger      => "[A]",
        HeroClass.Necromancer => "[N]",
        _                     => "[?]",
    };

    /// <summary>
    /// Replaces any character outside the printable ASCII range (32-126) with
    /// a safe substitute so SpriteBatch.DrawString never throws.
    /// Common replacements keep the text readable (em-dash -> hyphen, etc.).
    /// </summary>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is >= ' ' and <= '~')
                sb.Append(c);          // printable ASCII — keep as-is
            else if (c == '\u2014' || c == '\u2013')
                sb.Append('-');        // em-dash / en-dash -> hyphen
            else if (c == '\u2018' || c == '\u2019')
                sb.Append('\'');       // curly single quotes
            else if (c == '\u201C' || c == '\u201D')
                sb.Append('"');        // curly double quotes
            else
                sb.Append('?');        // unknown non-ASCII
        }
        return sb.ToString();
    }
}
