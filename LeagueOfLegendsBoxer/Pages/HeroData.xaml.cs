﻿using LeagueOfLegendsBoxer.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LeagueOfLegendsBoxer.Pages
{
    /// <summary>
    /// Interaction logic for HeroData.xaml
    /// </summary>
    public partial class HeroData : Page
    {
        public HeroData(HeroDataViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
