namespace WinFsTools;

public sealed class NoFocusCheckBox : CheckBox
{
    protected override bool ShowFocusCues => false;
}
