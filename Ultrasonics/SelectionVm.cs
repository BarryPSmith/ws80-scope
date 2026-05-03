using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    public class SelectionVmDsn : SelectionVm<SessionInfo> { }
    public class SelectionVm<T> : ObservableRecipient
    {
        public required List<T> Items { get; set; }

        T? _selectedItem;
        public T? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }
    }
}
