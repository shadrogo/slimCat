﻿#region Copyright

// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MessageService.cs">
//    Copyright (c) 2013, Justin Kadrovach, All rights reserved.
//   
//    This source is subject to the Simplified BSD License.
//    Please see the License.txt file for more information.
//    All other rights reserved.
//    
//    THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY 
//    KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//    IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
//    PARTICULAR PURPOSE.
// </copyright>
//  --------------------------------------------------------------------------------------------------------------------

#endregion

namespace slimCat.Services
{
    #region Usings

    using Microsoft.Practices.Prism.Events;
    using Models;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Windows;
    using System.Windows.Threading;
    using Utilities;
    using Commands = Utilities.Constants.ClientCommands;

    #endregion

    /// <summary>
    ///     The message service is responsible for handing user commands.
    /// </summary>
    public class MessageService : DispatcherObject
    {

        #region Fields

        private readonly IListConnection api;

        private readonly ICharacterManager characterManager;

        private readonly IDictionary<string, CommandHandler> commands;

        private readonly IChatConnection connection;

        private readonly IEventAggregator events;

        private readonly ILoggingService logger;

        private readonly IChatModel model;

        private readonly IChannelManager channelManager;

        #endregion

        #region Constructors and Destructors

        public MessageService(
            IEventAggregator events,
            IChatModel model,
            IChatConnection connection,
            IListConnection api,
            ICharacterManager manager,
            ILoggingService logger,
            IChannelManager channelManager)
        {

            try
            {
                this.events = events.ThrowIfNull("events");
                this.model = model.ThrowIfNull("model");
                this.connection = connection.ThrowIfNull("connection");
                this.api = api.ThrowIfNull("api");
                this.logger = logger.ThrowIfNull("logger");
                this.channelManager = channelManager.ThrowIfNull("channelManager");
                characterManager = manager.ThrowIfNull("characterManager");

                this.events.GetEvent<UserCommandEvent>().Subscribe(CommandReceived, ThreadOption.UIThread, true);

                commands = new Dictionary<string, CommandHandler>
                    {
                        {"priv", OnPrivRequested},
                        {Commands.UserMessage, OnPriRequested},
                        {Commands.ChannelMessage, OnMsgRequested},
                        {Commands.ChannelAd, OnLrpRequested},
                        {Commands.UserStatus, OnStatusChangeRequested},
                        {"close", OnCloseRequested},
                        {"forceclose", OnForceChannelCloseRequested},
                        {"join", OnJoinRequested},
                        {Commands.UserIgnore, OnIgnoreRequested},
                        {"clear", OnClearRequested},
                        {"clearall", OnClearAllRequested},
                        {"_logger_open_log", OnOpenLogRequested},
                        {"_logger_open_folder", OnOpenLogFolderRequested},
                        {"code", OnChannelCodeRequested},
                        {"_snap_to_last_update", OnNotificationFocusRequested},
                        {Commands.UserInvite, OnInviteToChannelRequested},
                        {"who", OnWhoInformationRequested},
                        {"getdescription", OnChannelDescriptionRequested},
                        {"interesting", OnMarkInterestedRequested},
                        {"notinteresting", OnMarkNotInterestedRequested},
                        {"ignoreUpdates", OnIgnoreUpdatesRequested},
                        {Commands.AdminAlert, OnReportRequested},
                        {"tempignore", OnTemporaryIgnoreRequested},
                        {"tempunignore", OnTemporaryIgnoreRequested},
                        {"tempinteresting", OnTemporaryInterestingRequested},
                        {"tempnotinteresting", OnTemporaryInterestingRequested},
                        {"handlelatest", OnHandleLatestReportRequested},
                        {"handlereport", OnHandleLatestReportByUserRequested},
                        {"rejoin", OnChannelRejoinRequested },
                        {"searchtag", OnSearchTagToggleRequested}
                    };
            }
            catch (Exception ex)
            {
                ex.Source = "Message Daemon, init";
                Exceptions.HandleException(ex);
            }
        }

        #endregion

        #region Delegates

        private delegate void CommandHandler(IDictionary<string, object> command);

        #endregion

        #region Methods

        private static void ClearChannel(ChannelModel channel)
        {
            foreach (var item in channel.Messages)
                item.Dispose();

            foreach (var item in channel.Ads)
                item.Dispose();

            channel.Messages.Clear();
            channel.Ads.Clear();
        }

        private void OnPrivRequested(IDictionary<string, object> command)
        {
            var characterName = command.Get(Constants.Arguments.Character);
            if (model.CurrentCharacter.NameEquals(characterName))
                events.GetEvent<ErrorEvent>().Publish("Hmmm... talking to yourself?");
            else
            {
                var guess = characterManager.SortedCharacters.OrderBy(x => x.Name)
                    .Where(x => !x.NameEquals(model.CurrentCharacter.Name))
                    .FirstOrDefault(c => c.Name.StartsWith(characterName, true, null));

                channelManager.JoinChannel(ChannelType.PrivateMessage, guess == null ? characterName : guess.Name);
            }
        }

        private void OnPriRequested(IDictionary<string, object> command)
        {
            channelManager.AddMessage(
                command.Get(Constants.Arguments.Message), 
                command.Get("recipient"),
                Constants.Arguments.ThisCharacter);
            connection.SendMessage(command);
        }

        private void OnMsgRequested(IDictionary<string, object> command)
        {
            channelManager.AddMessage(
                command.Get(Constants.Arguments.Message),
                command.Get(Constants.Arguments.Channel),
                Constants.Arguments.ThisCharacter);
            connection.SendMessage(command);
        }

        private void OnLrpRequested(IDictionary<string, object> command)
        {
            channelManager.AddMessage(
                command.Get(Constants.Arguments.Message)
                , command.Get(Constants.Arguments.Channel),
                Constants.Arguments.ThisCharacter,
                MessageType.Ad);
            connection.SendMessage(command);
        }

        private void OnStatusChangeRequested(IDictionary<string, object> command)
        {
            var statusmsg = command.Get(Constants.Arguments.StatusMessage);
            var status = command.Get(Constants.Arguments.Status).ToEnum<StatusType>();

            model.CurrentCharacter.Status = status;
            model.CurrentCharacter.StatusMessage = statusmsg;
            connection.SendMessage(command);
        }

        private void OnCloseRequested(IDictionary<string, object> command)
        {
            channelManager.RemoveChannel(command.Get(Constants.Arguments.Channel));
        }

        private void OnJoinRequested(IDictionary<string, object> command)
        {
            var channels = command.Get(Constants.Arguments.Channel);

            if (model.CurrentChannels.FirstByIdOrNull(channels) != null)
            {
                events.GetEvent<RequestChangeTabEvent>().Publish(channels);
                return;
            }

            var guess =
                model.AllChannels.OrderBy(channel => channel.Title)
                    .FirstOrDefault(channel => channel.Title.StartsWith(channels, true, null));

            var toJoin = guess != null ? guess.Id : channels;
            var toSend = new {channel = toJoin};

            connection.SendMessage(toSend, Commands.ChannelJoin);
        }

        private void OnSearchTagToggleRequested(IDictionary<string, object> command)
        {
            var character = command.Get(Constants.Arguments.Character);

            if (characterManager.IsOnList(character, ListKind.SearchResult, false))
            {
                characterManager.Remove(character, ListKind.SearchResult);
                return;
            }

            characterManager.Add(character, ListKind.SearchResult);
        }

        private void OnIgnoreRequested(IDictionary<string, object> command)
        {
            var args = command.Get(Constants.Arguments.Character);

            var action = command.Get(Constants.Arguments.Action);

            if (action == Constants.Arguments.ActionAdd)
                characterManager.Add(args, ListKind.Ignored);
            else if (action == Constants.Arguments.ActionDelete)
                characterManager.Remove(args, ListKind.Ignored);
            else
                return;

            events.GetEvent<NewUpdateEvent>()
                .Publish(
                    new CharacterUpdateModel(
                        characterManager.Find(args),
                        new CharacterUpdateModel.ListChangedEventArgs
                            {
                                IsAdded = action == Constants.Arguments.ActionAdd,
                                ListArgument = ListKind.Ignored
                            }));

            connection.SendMessage(command);
        }

        private void OnClearRequested(IDictionary<string, object> command)
        {
            ClearChannel(model.CurrentChannel);
        }

        private void OnClearAllRequested(IDictionary<string, object> command)
        {
            model.CurrentChannels
                .Cast<ChannelModel>()
                .Union(model.CurrentPms)
                .Each(ClearChannel);
        }

        private void OnOpenLogRequested(IDictionary<string, object> command)
        {
            logger.OpenLog(false, model.CurrentChannel.Title, model.CurrentChannel.Id);
        }

        private void OnOpenLogFolderRequested(IDictionary<string, object> command)
        {
            if (command.ContainsKey(Constants.Arguments.Channel))
            {
                var toOpen = command.Get(Constants.Arguments.Channel);
                if (!string.IsNullOrWhiteSpace(toOpen))
                    logger.OpenLog(true, toOpen, toOpen);
            }
            else
                logger.OpenLog(true, model.CurrentChannel.Title, model.CurrentChannel.Id);
        }

        private void OnChannelCodeRequested(IDictionary<string, object> command)
        {
            if (model.CurrentChannel.Id.Equals("Home", StringComparison.OrdinalIgnoreCase))
            {
                events.GetEvent<ErrorEvent>().Publish("Home channel does not have a code.");
                return;
            }

            var toCopy = "[session={0}]{1}[/session]".FormatWith(
                model.CurrentChannel.Title,
                model.CurrentChannel.Id);

            Clipboard.SetData(DataFormats.Text, toCopy);
            events.GetEvent<ErrorEvent>().Publish("Channel's code copied to clipboard.");
        }

        private void OnNotificationFocusRequested(IDictionary<string, object> command)
        {
            string target = null;
            string kind = null;

            if (command.ContainsKey("target"))
                target = command.Get("target");

            if (command.ContainsKey("kind"))
                kind = command.Get("kind");

            // first off, see if we have a target defined. If we do, then let's see if it's one of our current channels
            if (target != null)
            {
                if (target.StartsWith("http://"))
                {
                    // if our target is a command to get the latest link-able thing, let's grab that
                    Process.Start(target);
                    return;
                }

                if (kind != null && kind.Equals(Constants.Arguments.Report))
                {
                    command.Clear();
                    command[Constants.Arguments.Name] = target;
                    OnHandleLatestReportByUserRequested(command);
                }

                var channel = (ChannelModel) model.CurrentPms.FirstByIdOrNull(target)
                              ?? model.CurrentChannels.FirstByIdOrNull(target);

                if (channel != null)
                {
                    events.GetEvent<RequestChangeTabEvent>().Publish(target);
                    Dispatcher.Invoke((Action) NotificationService.ShowWindow);
                    return;
                }
            }

            var latest = model.Notifications.LastOrDefault();

            // if we got to this point our notification is doesn't involve an active tab
            if (latest == null)
                return;

            var newCharacterUpdate = latest as CharacterUpdateModel;
            if (newCharacterUpdate != null)
            {
                // so tell our system to join the Pm Tab
                channelManager.JoinChannel(ChannelType.PrivateMessage, newCharacterUpdate.TargetCharacter.Name);

                Dispatcher.Invoke((Action) NotificationService.ShowWindow);
                return;
            }

            var stuffWith = latest as ChannelUpdateModel;
            if (stuffWith == null)
                return;

            var doStuffWith = stuffWith;
            var newChannel = model.AllChannels.FirstByIdOrNull(doStuffWith.TargetChannel.Id);

            if (newChannel == null)
            {
                // if it's null, then we've got an invite to a new channel
                var toSend = new {channel = doStuffWith.TargetChannel.Id};
                connection.SendMessage(toSend, Commands.ChannelJoin);
                Dispatcher.Invoke((Action) NotificationService.ShowWindow);
                return;
            }

            var chanType = newChannel.Type;
            channelManager.JoinChannel(chanType, doStuffWith.TargetChannel.Id);
            Dispatcher.Invoke((Action) NotificationService.ShowWindow);
        }

        private void OnInviteToChannelRequested(IDictionary<string, object> command)
        {
            if (command.ContainsKey(Constants.Arguments.Character) && command.Get(Constants.Arguments.Character).Equals(
                model.CurrentCharacter.Name, StringComparison.OrdinalIgnoreCase))
            {
                events.GetEvent<ErrorEvent>().Publish("You don't need my help to talk to yourself.");
                return;
            }

            connection.SendMessage(command);
        }

        private void OnWhoInformationRequested(IDictionary<string, object> command)
        {
            events.GetEvent<ErrorEvent>()
                .Publish(
                    "Server, server, across the sea,\nWho is connected, most to thee?\nWhy, "
                    + model.CurrentCharacter.Name + " be!");
        }

        private void OnChannelDescriptionRequested(IDictionary<string, object> command)
        {
            if (model.CurrentChannel.Id.Equals("Home", StringComparison.OrdinalIgnoreCase))
            {
                events.GetEvent<ErrorEvent>()
                    .Publish("Poor home channel, with no description to speak of...");
                return;
            }

            if (model.CurrentChannel is GeneralChannelModel)
            {
                Clipboard.SetData(
                    DataFormats.Text, (model.CurrentChannel as GeneralChannelModel).Description);
                events.GetEvent<ErrorEvent>()
                    .Publish("Channel's description copied to clipboard.");
            }
            else
                events.GetEvent<ErrorEvent>().Publish("Hey! That's not a channel.");
        }

        private void OnMarkInterestedRequested(IDictionary<string, object> command)
        {
            var args = command.Get(Constants.Arguments.Character);
            var isAdd = !characterManager.IsOnList(args, ListKind.Interested);
            if (isAdd)
                characterManager.Add(args, ListKind.Interested);
            else
                characterManager.Remove(args, ListKind.Interested);

            events.GetEvent<NewUpdateEvent>()
                .Publish(
                    new CharacterUpdateModel(
                        characterManager.Find(args),
                        new CharacterUpdateModel.ListChangedEventArgs
                            {
                                IsAdded = isAdd,
                                ListArgument = ListKind.Interested
                            }));
        }

        private void OnMarkNotInterestedRequested(IDictionary<string, object> command)
        {
            var args = command.Get(Constants.Arguments.Character);

            var isAdd = !characterManager.IsOnList(args, ListKind.NotInterested);
            if (isAdd)
                characterManager.Add(args, ListKind.NotInterested);
            else
                characterManager.Remove(args, ListKind.NotInterested);

            events.GetEvent<NewUpdateEvent>()
                .Publish(
                    new CharacterUpdateModel(
                        characterManager.Find(args),
                        new CharacterUpdateModel.ListChangedEventArgs
                            {
                                IsAdded = isAdd,
                                ListArgument = ListKind.NotInterested
                            }));
        }

        private void OnIgnoreUpdatesRequested(IDictionary<string, object> command)
        {
            var args = command.Get(Constants.Arguments.Character);

            var isAdd = !characterManager.IsOnList(args, ListKind.IgnoreUpdates);
            if (isAdd)
                characterManager.Add(args, ListKind.IgnoreUpdates);
            else
                characterManager.Remove(args, ListKind.IgnoreUpdates);
        }

        private void OnReportRequested(IDictionary<string, object> command)
        {
            if (!command.ContainsKey(Constants.Arguments.Report))
                command.Add(Constants.Arguments.Report, string.Empty);

            var logId = -1; // no log

            // report format: "Current Tab/Channel: <channel> | Reporting User: <reported user> | <report body>
            var reportText = string.Format(
                "Current Tab/Channel: {0} | Reporting User: {1} | {2}",
                command.Get(Constants.Arguments.Channel),
                command.Get(Constants.Arguments.Name),
                command.Get(Constants.Arguments.Report));

            // upload on a worker thread to avoid blocking
            new Thread(() =>
                {
                    Thread.CurrentThread.IsBackground = true;

                    var channelText = command.Get(Constants.Arguments.Channel);
                    if (!string.IsNullOrWhiteSpace(channelText) && !channelText.Equals("None"))
                    {
                        // we could just use _model.SelectedChannel, but the user might change tabs immediately after reporting, creating a race condition
                        ChannelModel channel;
                        if (channelText == command.Get(Constants.Arguments.Name))
                            channel = model.CurrentPms.FirstByIdOrNull(channelText);
                        else
                            channel = model.CurrentChannels.FirstByIdOrNull(channelText);

                        if (channel != null)
                        {
                            var report = new ReportModel
                            {
                                Reporter = model.CurrentCharacter,
                                Reported = command.Get(Constants.Arguments.Name),
                                Complaint = command.Get(Constants.Arguments.Report),
                                Tab = channelText
                            };


                            logId = api.UploadLog(report, channel.Messages.Union(channel.Ads));
                        }
                    }

                    command.Remove(Constants.Arguments.Name);
                    command[Constants.Arguments.Report] = reportText;
                    command[Constants.Arguments.LogId] = logId;

                    if (!command.ContainsKey(Constants.Arguments.Action))
                        command[Constants.Arguments.Action] = Constants.Arguments.ActionReport;

                    connection.SendMessage(command);
                }).Start();

        }

        private void OnTemporaryIgnoreRequested(IDictionary<string, object> command)
        {
            var character = command.Get(Constants.Arguments.Character).ToLower().Trim();
            var add = command.Get(Constants.Arguments.Type) == "tempignore";

            if (add)
                characterManager.Add(character, ListKind.Ignored, true);
            else
                characterManager.Remove(character, ListKind.Ignored, true);
        }

        private void OnTemporaryInterestingRequested(IDictionary<string, object> command)
        {
            var character = command.Get(Constants.Arguments.Character).ToLower().Trim();
            var add = command.Get(Constants.Arguments.Type) == "tempinteresting";

            characterManager.Add(
                character,
                add ? ListKind.Interested : ListKind.NotInterested,
                true);
        }

        private void OnChannelRejoinRequested(IDictionary<string, object> command)
        {
            var channelName = command.Get(Constants.Arguments.Channel);
            channelManager.RemoveChannel(channelName, true);

            var toSend = new { channel = channelName };
            connection.SendMessage(toSend, Commands.ChannelJoin);
        }

        private void OnForceChannelCloseRequested(IDictionary<string, object> command)
        {
            var channelName = command.Get(Constants.Arguments.Channel);
            channelManager.RemoveChannel(channelName, true);
        }

        private void OnHandleLatestReportRequested(IDictionary<string, object> command)
        {
            command.Clear();
            var latest = (from n in model.Notifications
                let update = n as CharacterUpdateModel
                where update != null
                      && update.Arguments is CharacterUpdateModel.ReportFiledEventArgs
                select update).FirstOrDefault();

            if (latest == null)
                return;

            var args = latest.Arguments as CharacterUpdateModel.ReportFiledEventArgs;

            command.Add(Constants.Arguments.Type, Commands.AdminAlert);
            if (args != null) command.Add(Constants.Arguments.CallId, args.CallId);
            command.Add(Constants.Arguments.Action, Constants.Arguments.ActionConfirm);

            channelManager.JoinChannel(ChannelType.PrivateMessage, latest.TargetCharacter.Name);

            var logId = -1;
            if (command.ContainsKey(Constants.Arguments.LogId))
                int.TryParse(command.Get(Constants.Arguments.LogId), out logId);

            if (logId != -1)
                Process.Start(Constants.UrlConstants.ReadLog + logId);

            connection.SendMessage(command);
        }

        private void OnHandleLatestReportByUserRequested(IDictionary<string, object> command)
        {
            if (command.ContainsKey(Constants.Arguments.Name))
            {
                var target = characterManager.Find(command.Get(Constants.Arguments.Name));

                if (!target.HasReport)
                {
                    events.GetEvent<ErrorEvent>()
                        .Publish("Cannot find report for specified character!");
                    return;
                }

                command[Constants.Arguments.Type] = Commands.AdminAlert;
                command.Add(Constants.Arguments.CallId, target.LastReport.CallId);
                if (!command.ContainsKey(Constants.Arguments.Action))
                    command[Constants.Arguments.Action] = Constants.Arguments.ActionConfirm;

                channelManager.JoinChannel(ChannelType.PrivateMessage, target.Name);

                var logId = -1;
                if (command.ContainsKey(Constants.Arguments.LogId))
                    int.TryParse(command.Get(Constants.Arguments.LogId), out logId);

                if (logId != -1)
                    Process.Start(Constants.UrlConstants.ReadLog + logId);

                connection.SendMessage(command);
            }

            OnHandleLatestReportRequested(command);
        }

        private void CommandReceived(IDictionary<string, object> command)
        {
            var type = command.Get(Constants.Arguments.Type);

            if (type == null)
                return;

            try
            {
                var useSlash = type.ToLower().Equals(type);
                Logging.Log((useSlash ? "/" : "") + type, "user cmnd");
                Logging.LogObject(command);
                Logging.Log();

                CommandHandler handler;
                commands.TryGetValue(type, out handler);
                if (handler == null)
                {
                    connection.SendMessage(command);
                    return;
                }

                handler(command);
            }
            catch (Exception ex)
            {
                ex.Source = "Message Daemon, command received";
                Exceptions.HandleException(ex);
            }
        }


        [Conditional("DEBUG")]
        private void Log(string text)
        {
            Logging.LogLine(text, "msg serv");
        }
        #endregion
    }
}