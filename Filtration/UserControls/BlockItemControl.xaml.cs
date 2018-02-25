﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Filtration.Annotations;
using Filtration.ObjectModel;
using Filtration.ObjectModel.BlockItemBaseTypes;
using Filtration.Views;
using GalaSoft.MvvmLight.CommandWpf;
using Xceed.Wpf.Toolkit;

namespace Filtration.UserControls
{
    public partial class BlockItemControl : INotifyPropertyChanged
    {
        public BlockItemControl()
        {
            InitializeComponent();
            // ReSharper disable once PossibleNullReferenceException
            (Content as FrameworkElement).DataContext = this;

            SetBlockColorCommand = new RelayCommand(OnSetBlockColorCommmand);
        }

        public RelayCommand SetBlockColorCommand { get; private set; }

        public static readonly DependencyProperty BlockItemProperty = DependencyProperty.Register(
            "BlockItem",
            typeof(IItemFilterBlockItem),
            typeof(BlockItemControl),
            new FrameworkPropertyMetadata());
        
        public static readonly DependencyProperty RemoveItemCommandProperty = DependencyProperty.Register(
            "RemoveItemCommand",
            typeof(RelayCommand<IItemFilterBlockItem>),
            typeof(BlockItemControl),
            new FrameworkPropertyMetadata());

        public static readonly DependencyProperty RemoveEnabledProperty = DependencyProperty.Register(
            "RemoveEnabled",
            typeof(Visibility),
            typeof(BlockItemControl),
            new FrameworkPropertyMetadata());

        public IItemFilterBlockItem BlockItem
        {
            get
            {
                return (IItemFilterBlockItem)GetValue(BlockItemProperty);
            }
            set
            {
                SetValue(BlockItemProperty, value);
                OnPropertyChanged();
            }
        }

        public RelayCommand<IItemFilterBlockItem> RemoveItemCommand
        {
            get
            {
                return (RelayCommand<IItemFilterBlockItem>)GetValue(RemoveItemCommandProperty);
            }
            set
            {
                SetValue(RemoveItemCommandProperty, value);
            }
        }
        public Visibility RemoveEnabled
        {
            get
            {
                return (Visibility)GetValue(RemoveEnabledProperty);
            }
            set
            {
                SetValue(RemoveEnabledProperty, value);
            }
        }


        public ObservableCollection<ColorItem> AvailableColors => PathOfExileColors.DefaultColors;

        public List<string> SoundsAvailable => new List<string> {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16",
            "ShGeneral", "ShBlessed", "ShChaos", "ShDivine", "ShExalted", "ShMirror", "ShAlchemy",
            "ShFusing", "ShRegal", "ShVaal"
        };

        private void OnSetBlockColorCommmand()
        {
            var blockItem = BlockItem as ColorBlockItem;
            if (blockItem?.ThemeComponent == null) return;

            blockItem.Color = blockItem.ThemeComponent.Color;
        }
        
        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
