﻿using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CUETools.Codecs
{
    public class EncoderListViewModel : BindingList<AudioEncoderSettingsViewModel>
    {
        private List<IAudioEncoderSettings> model;

        public EncoderListViewModel(List<IAudioEncoderSettings> model)
            : base()
        {
            this.model = model;
            model.ForEach(item => Add(new AudioEncoderSettingsViewModel(item)));
            AddingNew += OnAddingNew;
            ListChanged += OnListChanged;
        }

        private void OnAddingNew(object sender, AddingNewEventArgs e)
        {
            var item = new CommandLine.EncoderSettings("new", "wav", true, "", "", "", "");
            model.Add(item);
            e.NewObject = new AudioEncoderSettingsViewModel(item);
        }

        private void OnListChanged(object sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType == ListChangedType.ItemDeleted)
            {
                model.RemoveAt(e.NewIndex);
            }
        }

        public bool TryGetValue(string extension, bool lossless, string name, out AudioEncoderSettingsViewModel result)
        {
            //result = this.Where(udc => udc.settings.Extension == extension && udc.settings.Lossless == lossless && udc.settings.Name == name).First();
            foreach (AudioEncoderSettingsViewModel udc in this)
            {
                if (udc.Settings.Extension == extension && udc.Settings.Lossless == lossless && udc.Settings.Name == name)
                {
                    result = udc;
                    return true;
                }
            }
            result = null;
            return false;
        }

        public AudioEncoderSettingsViewModel GetDefault(string extension, bool lossless)
        {
            AudioEncoderSettingsViewModel result = null;
            foreach (AudioEncoderSettingsViewModel udc in this)
            {
                if (udc.Settings.Extension == extension && udc.Settings.Lossless == lossless && (result == null || result.Settings.Priority < udc.Settings.Priority))
                {
                    result = udc;
                }
            }
            return result;
        }
    }
}
