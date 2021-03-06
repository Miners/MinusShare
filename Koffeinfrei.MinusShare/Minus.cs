﻿//  Koffeinfrei Minus Share
//  Copyright (C) 2011  Alexis Reigel
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using BiasedBit.MinusEngine;
using Koffeinfrei.Base;
using Koffeinfrei.MinusShare.Properties;
using Resources = Koffeinfrei.MinusShare.Properties.Resources;
using System.Linq;

namespace Koffeinfrei.MinusShare
{
    public class Minus
    {
        private const String ApiKey = "dummyKey";
        private const string BaseUrl = "http://min.us/m";
        private const string UrlUnavailable = "Unavailable";
        private const string GalleryDeleted = "Deleted";

        private readonly List<string> queuedFiles;
        private readonly List<String> uploadedFiles;
        private string title;

        private readonly List<string> recipients;
        private readonly MinusApi api;

        private string cookie;

        public LoginStatus LoginStatus { get; set; }

        public Action<string> InfoLogger { get; set; }
        public Action<string> ErrorLogger { get; set; }

        public Minus()
        {
            queuedFiles = new List<string>();
            uploadedFiles = new List<String>();

            recipients = new List<string>();

            api = new MinusApi(ApiKey);

            LoginStatus = LoginStatus.None;
        }

        public void AddFiles(List<string> files)
        {
            queuedFiles.AddRange(files);
        }

        public void RemoveFile(string fileName)
        {
            queuedFiles.Remove(fileName);
        }

        public void AddRecipients(List<string> recipients)
        {
            this.recipients.AddRange(recipients);
        }

        public void SetTitle(string title)
        {
            this.title = title;
        }

        public void Share(Action<MinusResult.Share> galleryCreated)
        {
            // create a couple of things we're going to need between requests
            CreateGalleryResult galleryCreatedResult = null;

            // set up the listeners for CREATE
            api.CreateGalleryFailed += (sender, e) => LogError(Resources.CreateGalleryFailed, e);

            api.CreateGalleryComplete += (sender, result) =>
            {
                // gallery created, trigger upload of the first file
                galleryCreatedResult = result;
                LogInfo(Resources.GalleryCreated);
                LogInfo(Resources.UploadingFiles);
                FileInfo file = new FileInfo(queuedFiles[uploadedFiles.Count]);
                LogInfo(Resources.UploadingFile + file.Name + "...");
                api.UploadItem(result.EditorId, result.Key, queuedFiles[0]);
            };

            // set up the listeners for UPLOAD
            api.UploadItemFailed += (sender, e) => LogError(Resources.UploadFailed, e);

            api.UploadItemComplete += (sender, result) =>
            {
                // upload complete, either trigger another upload or save the gallery if all files have been uploaded
                LogInfo(Resources.UploadSuccessful);
                uploadedFiles.Add(result.Id);
                if (uploadedFiles.Count == queuedFiles.Count)
                {
                    // if all the elements are uploaded, then save the gallery
                    LogInfo(Resources.AllUploadSuccessful);
                    api.SaveGallery(title ?? "", galleryCreatedResult.EditorId, galleryCreatedResult.Key, uploadedFiles.ToArray());
                }
                else
                {
                    // otherwise just keep uploading
                    FileInfo file = new FileInfo(queuedFiles[uploadedFiles.Count]);
                    LogInfo(Resources.UploadingFile + file.Name + "...");
                    api.UploadItem(galleryCreatedResult.EditorId, galleryCreatedResult.Key, file.FullName);
                }
            };

            // set up the listeners for SAVE
            api.SaveGalleryFailed += (sender, e) => LogError(Resources.SaveGalleryFailed, e);

            api.SaveGalleryComplete += sender =>
            {
                string readUrl = BaseUrl + galleryCreatedResult.ReaderId;
                string editUrl = BaseUrl + galleryCreatedResult.EditorId;

                LogInfo(Resources.GallerySaved);
                galleryCreated(new MinusResult.Share
                {
                    EditUrl = editUrl,
                    ShareUrl = readUrl
                });
            };

            if (LoginStatus == LoginStatus.Successful)
            {
                api.CreateGallery(cookie);
            }
            else
            {
                api.CreateGallery();
            }
        }

        public void GetGalleries(Action<List<MinusResult.Galleries>> gotGalleries)
        {
            if (LoginStatus == LoginStatus.Successful)
            {
                //set up listeners for MyGalleries
                api.MyGalleriesFailed += (sender, e) => LogError(Resources.GetGalleriesFailed, e);
                api.MyGalleriesComplete += (sender, result) =>
                {
                    LogInfo(Resources.GetGalleriesSuccessful);

                    List<MinusResult.Galleries> galleries = result.Galleries.Select(gallery => new MinusResult.Galleries
                    {
                        EditUrl = gallery.EditorId == UrlUnavailable || gallery.EditorId == GalleryDeleted ? null : BaseUrl + gallery.EditorId,
                        ItemCount = gallery.ItemCount,
                        Name = gallery.Name,
                        ShareUrl = gallery.ReaderId == UrlUnavailable || gallery.ReaderId == GalleryDeleted ? null : BaseUrl + gallery.ReaderId,
                        Deleted = gallery.ReaderId == GalleryDeleted
                    }).ToList();

                    gotGalleries(galleries);
                };

                api.MyGalleries(cookie);
            }
            else
            {
                gotGalleries(new List<MinusResult.Galleries>());
            }
        }

        public void Login(Action<LoginStatus> loggedIn)
        {
            if (LoginStatus == LoginStatus.None)
            {
                // set up the listeners for SIGNIN
                api.SignInFailed += (sender, e) =>
                {
                    LogError(Resources.SignInFailed, e);
                    LoginStatus = LoginStatus.Failed;

                    loggedIn(LoginStatus);
                };
                api.SignInComplete += (sender, result) =>
                {
                    LogInfo(Resources.SignedIn);
                    LoginStatus = LoginStatus.Successful;

                    cookie = result.CookieHeaders;

                    loggedIn(LoginStatus);
                };

                if (!string.IsNullOrEmpty(Settings.Default.Username) && !string.IsNullOrEmpty(Settings.Default.Password))
                {
                    api.SignIn(Settings.Default.Username, KfEncryption.DecryptString(Settings.Default.Password).ToInsecureString());
                }
                else
                {
                    // no account set
                    LoginStatus = LoginStatus.Anonymous;

                    loggedIn(LoginStatus);
                }
            }
            else
            {
                loggedIn(LoginStatus);
            }
        }
        
        private void LogInfo(string message)
        {
            if (InfoLogger != null)
            {
                InfoLogger(message);
            }
        }

        private void LogError(string message, Exception exception)
        {
            if (ErrorLogger != null)
            {
                ErrorLogger(message + " " + exception.Message);
            }
        }
    }
}