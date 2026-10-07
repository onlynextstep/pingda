using System.Drawing;
using System.Windows.Forms;

namespace PingXu.App;

internal sealed class TrayAppearance : ProfessionalColorTable
{
    static Color Surface => Color.FromArgb(21,22,22);
    static Color Line => Color.FromArgb(65,70,58);
    static Color Acid => Color.FromArgb(220,255,66);
    public override Color ToolStripDropDownBackground => Surface;
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;
    public override Color MenuBorder => Line;
    public override Color MenuItemBorder => Acid;
    public override Color MenuItemSelected => Color.FromArgb(48,55,32);
    public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
    public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Surface;
}
