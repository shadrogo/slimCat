﻿namespace Slimcat.ViewModels
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Timers;

    using Microsoft.Practices.Prism.Events;
    using Microsoft.Practices.Prism.Regions;
    using Microsoft.Practices.Unity;

    using Slimcat.Models;
    using Slimcat.Services;
    using Slimcat.Utilities;
    using Slimcat.Views;

    #endregion

    /// <summary>
    ///     Used for most communications between users.
    /// </summary>
    public class PMChannelViewModel : ChannelViewModelBase
    {
        #region Fields

        private Timer checkTick = new Timer(5000);

        private Timer cooldownTimer = new Timer(500);

        private bool isInCoolDown;

        private bool isTyping;

        private FilteredCollection<IMessage, IViewableObject> messageManager;

        private int typingLengthCache;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="PMChannelViewModel" /> class.
        /// </summary>
        /// <param name="name">
        ///     The name.
        /// </param>
        /// <param name="contain">
        ///     The contain.
        /// </param>
        /// <param name="regman">
        ///     The regman.
        /// </param>
        /// <param name="events">
        ///     The events.
        /// </param>
        /// <param name="cm">
        ///     The cm.
        /// </param>
        public PMChannelViewModel(
            string name, IUnityContainer contain, IRegionManager regman, IEventAggregator events, IChatModel cm)
            : base(contain, regman, events, cm)
        {
            try
            {
                var temp = this.Container.Resolve<PMChannelModel>(name);
                this.Model = temp;

                this.Model.PropertyChanged += this.OnModelPropertyChanged;

                this.Container.RegisterType<object, PMChannelView>(
                    HelperConverter.EscapeSpaces(this.Model.Id), new InjectionConstructor(this));
                this.Events.GetEvent<NewUpdateEvent>()
                    .Subscribe(this.OnNewUpdateEvent, ThreadOption.PublisherThread, true, this.UpdateIsOurCharacter);

                this.cooldownTimer.Elapsed += (s, e) =>
                    {
                        this.isInCoolDown = false;
                        this.cooldownTimer.Enabled = false;
                        this.OnPropertyChanged("CanPost");
                    };

                this.checkTick.Elapsed += (s, e) =>
                    {
                        if (!this.IsTyping)
                        {
                            this.checkTick.Enabled = false;
                        }

                        if (this.Message != null && this.typingLengthCache == this.Message.Length)
                        {
                            this.IsTyping = false;
                            this.SendTypingNotification(TypingStatus.Paused);
                            this.checkTick.Enabled = false;
                        }

                        if (this.IsTyping)
                        {
                            this.typingLengthCache = this.Message != null ? this.Message.Length : 0;
                        }
                    };

                this.Model.Settings = SettingsDaemon.GetChannelSettings(
                    cm.CurrentCharacter.Name, this.Model.Title, this.Model.Id, this.Model.Type);

                this.ChannelSettings.Updated += (s, e) =>
                    {
                        this.OnPropertyChanged("ChannelSettings");
                        if (!this.ChannelSettings.IsChangingSettings)
                        {
                            SettingsDaemon.UpdateSettingsFile(
                                this.ChannelSettings, cm.CurrentCharacter.Name, this.Model.Title, this.Model.Id);
                        }
                    };

                this.messageManager = new FilteredCollection<IMessage, IViewableObject>(
                    this.Model.Messages, message => true);
            }
            catch (Exception ex)
            {
                ex.Source = "Utility Channel ViewModel, init";
                Exceptions.HandleException(ex);
            }
        }

        #endregion

        #region Public Events

        public event EventHandler StatusChanged;

        #endregion

        #region Public Properties

        /// <summary>
        ///     Gets a value indicating whether can post.
        /// </summary>
        public bool CanPost
        {
            get
            {
                return !this.isInCoolDown;
            }
        }

        /// <summary>
        ///     Gets the conversation with.
        /// </summary>
        public ICharacter ConversationWith
        {
            get
            {
                return this.ChatModel.IsOnline(this.Model.Id)
                           ? this.ChatModel.FindCharacter(this.Model.Id)
                           : new CharacterModel { Name = this.Model.Id };
            }
        }

        public ObservableCollection<IViewableObject> CurrentMessages
        {
            get
            {
                return this.messageManager.Collection;
            }
        }

        /// <summary>
        ///     Used for channel settings to display settings related to notify terms
        /// </summary>
        public bool HasNotifyTerms
        {
            get
            {
                return !string.IsNullOrEmpty(this.ChannelSettings.NotifyTerms);
            }
        }

        /// <summary>
        ///     Gets a value indicating whether has status.
        /// </summary>
        public bool HasStatus
        {
            get
            {
                return this.ConversationWith.StatusMessage.Length > 0;
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether is typing.
        /// </summary>
        public bool IsTyping
        {
            get
            {
                return this.isTyping;
            }

            set
            {
                this.isTyping = value;
                this.OnPropertyChanged("ShouldShowPostLength");
            }
        }

        /// <summary>
        ///     Gets a value indicating whether should show post length.
        /// </summary>
        public bool ShouldShowPostLength
        {
            get
            {
                return !string.IsNullOrEmpty(this.Message) && this.isTyping;
            }
        }

        /// <summary>
        ///     This is used for the channel settings, if it should show settings like 'notify when this character is mentioned'
        /// </summary>
        public bool ShowAllSettings
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        ///     Gets the status string.
        /// </summary>
        public string StatusString
        {
            get
            {
                if (!this.ChatModel.IsOnline(this.Model.Id))
                {
                    return string.Format("Warning: {0} is not online.", this.Model.Id);
                }

                switch (this.ConversationWith.Status)
                {
                    case StatusType.Away:
                        return string.Format("Warning: {0} is currently away.", this.Model.Id);
                    case StatusType.Busy:
                        return string.Format("Warning: {0} is currently busy.", this.Model.Id);
                    case StatusType.Idle:
                        return string.Format("Warning: {0} is currently idle.", this.Model.Id);
                    case StatusType.Looking:
                        return string.Format("{0} is looking for roleplay.", this.Model.Id);
                    case StatusType.Dnd:
                        return string.Format("Warning: {0} does not wish to be disturbed.", this.Model.Id);
                    case StatusType.Online:
                        return string.Format("{0} is online.", this.Model.Id);
                    case StatusType.Crown:
                        return string.Format(
                            "{0} has been a good person and has been rewarded with a crown!", this.Model.Id);
                }

                return this.ConversationWith.Status.ToString();
            }
        }

        /// <summary>
        ///     Gets the typing string.
        /// </summary>
        public string TypingString
        {
            get
            {
                var pm = (PMChannelModel)this.Model;

                if (!this.ChatModel.IsOnline(this.Model.Id))
                {
                    // visual indicator to help the user know when the other has gone offline
                    return string.Format("{0} is not online!", pm.Id);
                }

                switch (pm.TypingStatus)
                {
                    case TypingStatus.Typing:
                        return string.Format("{0} is typing " + pm.TypingString, pm.Id);
                    case TypingStatus.Paused:
                        return string.Format("{0} has entered text.", pm.Id);
                    default:
                        return string.Empty;
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        ///     The dispose.
        /// </summary>
        /// <param name="isManaged">
        ///     The is managed.
        /// </param>
        protected override void Dispose(bool isManaged)
        {
            if (isManaged)
            {
                this.checkTick.Dispose();
                this.cooldownTimer.Dispose();
                this.checkTick = null;
                this.cooldownTimer = null;

                this.StatusChanged = null;
                this.Events.GetEvent<NewUpdateEvent>().Unsubscribe(this.OnNewUpdateEvent);

                this.messageManager.Dispose();
                this.messageManager = null;
            }

            base.Dispose(isManaged);
        }

        /// <summary>
        ///     The on model property changed.
        /// </summary>
        /// <param name="sender">
        ///     The sender.
        /// </param>
        /// <param name="e">
        ///     The e.
        /// </param>
        protected override void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "TypingStatus" || e.PropertyName == "TypingString")
            {
                this.OnPropertyChanged("TypingString");
            }
        }

        /// <summary>
        ///     The on this property changed.
        /// </summary>
        /// <param name="sender">
        ///     The sender.
        /// </param>
        /// <param name="e">
        ///     The e.
        /// </param>
        protected override void OnThisPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "Message")
            {
                return;
            }

            if (string.IsNullOrEmpty(this.Message))
            {
                this.SendTypingNotification(TypingStatus.Clear);
                this.IsTyping = false;
            }
            else if (!this.IsTyping)
            {
                this.IsTyping = true;
                this.SendTypingNotification(TypingStatus.Typing);
                this.checkTick.Enabled = true;
            }
        }

        /// <summary>
        ///     The send message.
        /// </summary>
        protected override void SendMessage()
        {
            if (this.Message.Length > 50000)
            {
                this.UpdateError("I can't let you post that. That's way too big. Try again, buddy.");
            }
            else if (this.isInCoolDown)
            {
                this.UpdateError("Where's the fire, son? Slow it down.");
            }
            else if (string.IsNullOrWhiteSpace(this.Message))
            {
                this.UpdateError("Hmm. Did you ... did you write anything?");
            }

            var toSend =
                CommandDefinitions.CreateCommand(
                    CommandDefinitions.ClientSendPm, new List<string> { this.Message, this.ConversationWith.Name })
                                  .ToDictionary();

            this.Events.GetEvent<UserCommandEvent>().Publish(toSend);
            this.Message = string.Empty;

            this.isInCoolDown = true;
            this.cooldownTimer.Enabled = true;
            this.OnPropertyChanged("CanPost");
            this.IsTyping = false;
            this.checkTick.Enabled = false;
        }

        private void OnNewUpdateEvent(NotificationModel param)
        {
            this.OnPropertyChanged("ConversationWith");
            this.OnPropertyChanged("StatusString");
            this.OnPropertyChanged("HasStatus");
            this.OnPropertyChanged("CanPost");
            this.OnPropertyChanged("TypingString");

            var arguments = ((CharacterUpdateModel)param).Arguments;
            if (!(arguments is CharacterUpdateModel.PromoteDemoteEventArgs))
            {
                this.OnStatusChanged();
            }
        }

        private void OnStatusChanged()
        {
            if (this.StatusChanged != null)
            {
                this.StatusChanged(this, new EventArgs());
            }
        }

        private void SendTypingNotification(TypingStatus type)
        {
            var toSend =
                CommandDefinitions.CreateCommand(
                    CommandDefinitions.ClientSendTypingStatus, 
                    new List<string> { type.ToString(), this.ConversationWith.Name }).ToDictionary();

            this.Events.GetEvent<UserCommandEvent>().Publish(toSend);
        }

        /// <summary>
        ///     If the update is applicable to our PM tab
        /// </summary>
        /// <param name="param">
        ///     The param.
        /// </param>
        /// <returns>
        ///     The <see cref="bool" />.
        /// </returns>
        private bool UpdateIsOurCharacter(NotificationModel param)
        {
            var updateModel = param as CharacterUpdateModel;
            if (updateModel != null)
            {
                var args = updateModel.TargetCharacter;
                return args.Name.Equals(this.ConversationWith.Name, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        #endregion
    }
}