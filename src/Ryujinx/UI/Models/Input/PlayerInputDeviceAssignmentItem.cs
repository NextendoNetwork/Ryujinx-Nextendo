using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Common.Configuration.Hid;
using System;

namespace Ryujinx.Ava.UI.Models.Input
{
    public class PlayerInputDeviceAssignmentItem : BaseModel
    {
        public DeviceType DeviceType { get; init; }

        public string Id { get; init; }

        public string Name { get; init; }

        public AssignedInputDeviceType AssignedType =>
            DeviceType == DeviceType.Keyboard ? AssignedInputDeviceType.Keyboard : AssignedInputDeviceType.Controller;

        public bool IsAssigned
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        }

        public bool HasBoundProfileName => !string.IsNullOrWhiteSpace(BoundProfileName);

        public string BoundProfileName
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasBoundProfileName));
            }
        }

        public bool HasAssignedToPlayers => !string.IsNullOrWhiteSpace(AssignedToPlayers);

        /// <summary>
        /// Comma-separated list of player names (e.g. "Player 1, Player 3") 
        /// that have this device assigned. Empty if no other player uses it.
        /// </summary>
        public string AssignedToPlayers
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAssignedToPlayers));
            }
        }

        /// <summary>
        /// True when this device is assigned to another player and 
        /// AllowDuplicateDeviceAssignment is disabled, making it unclickable.
        /// </summary>
        public bool IsDisabledByOtherPlayer
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }
}
