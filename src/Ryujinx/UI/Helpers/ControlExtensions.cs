using Avalonia.Controls;

namespace Ryujinx.Ava.UI.Helpers
{
    public static class ControlExtensions
    {
        extension(Control ctrl)
        {
            public int GridRow
            {
                get => Grid.GetRow(ctrl);
                set => Grid.SetRow(ctrl, value);
            }

            public int GridColumn
            {
                get => Grid.GetColumn(ctrl);
                set => Grid.SetColumn(ctrl, value);
            }

            public int GridRowSpan
            {
                get => Grid.GetRowSpan(ctrl);
                set => Grid.SetRowSpan(ctrl, value);
            }

            public int GridColumnSpan
            {
                get => Grid.GetColumnSpan(ctrl);
                set => Grid.SetColumnSpan(ctrl, value);
            }

            public bool GridIsSharedSizeScope
            {
                get => Grid.GetIsSharedSizeScope(ctrl);
                set => Grid.SetIsSharedSizeScope(ctrl, value);
            }
        }
    }
}
