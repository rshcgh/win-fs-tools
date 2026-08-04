namespace WinFsTools;

public sealed class NoFocusListBox : ListBox
{
    protected override bool ShowFocusCues => false;
}
