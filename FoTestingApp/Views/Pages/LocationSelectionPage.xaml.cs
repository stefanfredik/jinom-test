using System;
using System.Windows;
using System.Windows.Controls;

namespace FoTestingApp.Views.Pages
{
    public partial class LocationSelectionPage : Page
    {
        public LocationSelectionPage()
        {
            InitializeComponent();
        }

        private void BtnSelectPop_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new NewTestPage("pop"));
        }

        private void BtnSelectCustomer_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new NewTestPage("customer"));
        }
    }
}
