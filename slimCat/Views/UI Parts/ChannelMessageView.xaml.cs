﻿#region Copyright

// <copyright file="ChannelMessageView.xaml.cs">
//     Copyright (c) 2013-2015, Justin Kadrovach, All rights reserved.
// 
//     This source is subject to the Simplified BSD License.
//     Please see the License.txt file for more information.
//     All other rights reserved.
// 
//     THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
//     KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//     IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
//     PARTICULAR PURPOSE.
// </copyright>

#endregion

namespace slimCat.Views
{
    #region Usings

    using System.Windows.Controls;
    using System.Windows.Input;
    using Utilities;

    #endregion

    /// <summary>
    ///     Interaction logic for MessageView.xaml
    /// </summary>
    public partial class MessageView
    {
        #region Constructors and Destructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageView" /> class.
        /// </summary>
        public MessageView()
        {
            InitializeComponent();
        }

        #endregion

        private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            sender.TryOpenRightClickMenuCommand<Grid>(1);
        }
    }
}