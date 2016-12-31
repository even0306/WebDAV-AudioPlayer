﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ByteSizeLib;
using CSCore.SoundOut;
using WebDav.AudioPlayer.Audio;
using WebDav.AudioPlayer.Client;
using WebDav.AudioPlayer.Models;
using WebDav.AudioPlayer.Properties;

namespace WebDav.AudioPlayer.UI
{
    public partial class MainForm : Form
    {
        private readonly AssemblyConfig _config;
        private readonly MyWebDavClient _client;
        private readonly Player _player;

        private CancellationTokenSource _cancelationTokenSource;
        private CancellationToken _cancelToken;

        public MainForm(AssemblyConfig config)
        {
            _config = config;

            InitializeComponent();

            Icon = Resources.icon;

            InitCancellationTokenSource();

            _client = new MyWebDavClient(config);

            _player = new Player(_client)
            {
                Log = Log,

                PlayStarted = (selectedIndex, selectedSongName) =>
                {
                    string text = string.Format("Playing : '{0}'", selectedSongName);
                    textBoxSong.Text = text;
                    Text = @"WebDAV-AudioPlayer " + text;

                    labelTotalTime.Text = string.Format(@"{0:hh\:mm\:ss}", _player.TotalTime);

                    trackBarSong.Maximum = (int)_player.TotalTime.TotalSeconds;

                    listView.SelectedIndices.Clear();
                    listView.SelectedIndices.Add(selectedIndex);
                },
                PlayContinue = selectedSongName =>
                {
                    string text = string.Format("Playing : '{0}'", selectedSongName);
                    textBoxSong.Text = text;
                    Text = @"WebDAV-AudioPlayer " + text;
                },
                PlayPaused = selectedSongName =>
                {
                    string text = string.Format("Pausing : '{0}'", selectedSongName);
                    textBoxSong.Text = text;
                    Text = @"WebDAV-AudioPlayer " + text;
                },
                PlayStopped = () =>
                {
                    trackBarSong.Value = 0;
                    trackBarSong.Maximum = 1;
                    labelCurrentTime.Text = labelTotalTime.Text = @"00:00:00";
                }
            };
        }

        private void InitCancellationTokenSource()
        {
            _cancelationTokenSource = new CancellationTokenSource();
            _cancelToken = _cancelationTokenSource.Token;
        }

        private void Log(string text)
        {
            txtLogging.AppendText(string.Format("{0} - {1}\r\n", DateTime.Now, text));
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            RefreshTreeAsync();
        }

        private async Task RefreshTreeAsync()
        {
            var current = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;

            treeView.Nodes.Clear();

            var list = await _client.ListResourcesAsync(null, _cancelToken);
            if (list == null)
                return;

            var rootNode = new TreeNode
            {
                Text = _config.RootFolder,
                Tag = null
            };

            PopulateTree(ref rootNode, list);

            treeView.Nodes.Add(rootNode);
            rootNode.Expand();
            Cursor.Current = current;
        }

        private void PopulateTree(ref TreeNode node, IList<ResourceItem> resourceItems)
        {
            if (resourceItems == null)
                return;

            if (node == null)
            {
                node = new TreeNode
                {
                    Text = _config.RootFolder,
                    Tag = null
                };
            }

            foreach (var resourceItem in resourceItems.Where(r => r.IsCollection))
            {
                var childNode = new TreeNode
                {
                    Text = resourceItem.DisplayName,
                    Tag = resourceItem,
                    ImageKey = @"Folder",
                    SelectedImageKey = @"Folder"
                };

                PopulateTree(ref childNode, resourceItem.Items);
                node.Nodes.Add(childNode);
            }
        }

        private async void treeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var hitTest = e.Node.TreeView.HitTest(e.Location);
            if (hitTest.Location == TreeViewHitTestLocations.PlusMinus)
                return;

            var resourceItem = e.Node.Tag as ResourceItem;
            if (resourceItem == null)
                return;

            var current = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;

            var items = await _client.ListResourcesAsync(resourceItem.FullPath, _cancelToken);
            //var items = resourceItem.Items;
            if (items == null)
                return;

            var node = e.Node;
            node.Nodes.Clear();
            PopulateTree(ref node, items);
            node.Expand();

            _player.Items = items.Where(r => !r.IsCollection).ToList();

            listView.Items.Clear();
            foreach (var file in _player.Items)
            {
                string size = file.ContentLength != null ? ByteSize.FromBytes(file.ContentLength.Value).ToString("0.00 MB") : string.Empty;
                var listViewItem = new ListViewItem(new[] { file.DisplayName, size }) { Tag = file };
                listView.Items.Add(listViewItem);
            }

            Cursor.Current = current;
        }

        private void listView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            _player.Play(listView.SelectedIndices[0], _cancelToken);
        }

        private void buttonPlay_Click(object sender, EventArgs e)
        {
            _player.Play(listView.SelectedIndices[0], _cancelToken);
        }

        private void buttonPause_Click(object sender, EventArgs e)
        {
            _player.Pause();
        }

        private void buttonStop_Click(object sender, EventArgs e)
        {
            _cancelationTokenSource.Cancel();
            _player.Stop(true);
            InitCancellationTokenSource();
        }

        private void btnPrevious_Click(object sender, EventArgs e)
        {
            _player.Previous(_cancelToken);
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            _player.Next(_cancelToken);
        }

        private void trackBarSong_Scroll(object sender, EventArgs e)
        {
            _player.JumpTo(TimeSpan.FromSeconds(trackBarSong.Value));
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshTreeAsync();
        }


        private void trackBarSong_MouseDown(object sender, MouseEventArgs e)
        {
            _player.SetVolume(0);
        }

        private void trackBarSong_MouseUp(object sender, MouseEventArgs e)
        {
            _player.SetVolume(1);
        }

        private void audioPlaybackTimer_Tick(object sender, EventArgs e)
        {
            if (_player != null)
            {
                labelCurrentTime.Text = string.Format(@"{0:hh\:mm\:ss}", _player.CurrentTime);

                if (_player.PlaybackState == PlaybackState.Playing)
                {
                    trackBarSong.Value = (int)_player.CurrentTime.TotalSeconds;

                    if (_player.CurrentTime.Add(TimeSpan.FromMilliseconds(500)) > _player.TotalTime)
                    {
                        _player.Next(_cancelToken);
                    }
                }
            }
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            _player.Dispose();

            base.Dispose(disposing);
        }
    }
}