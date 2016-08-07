#region Copyright (C) 2007-2015 Team MediaPortal

/*
    Copyright (C) 2007-2015 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using MediaPortal.Common;
using MediaPortal.Common.Localization;
using MediaPortal.Common.Logging;
using MediaPortal.Common.MediaManagement.Helpers;
using MediaPortal.Extensions.OnlineLibraries.Matches;
using System.Collections.Generic;
using System.Reflection;
using MediaPortal.Common.MediaManagement.DefaultItemAspects;
using MediaPortal.Extensions.OnlineLibraries.Wrappers;
using MediaPortal.Extensions.UserServices.FanArtService.Interfaces;
using MediaPortal.Common.Threading;
using MediaPortal.Extensions.OnlineLibraries.Libraries.Common;
using System.Linq;

namespace MediaPortal.Extensions.OnlineLibraries.Matchers
{
  public abstract class MusicMatcher<TImg, TLang> : BaseMatcher<TrackMatch, string>
  {
    public class MusicMatcherSettings
    {
      public string LastRefresh { get; set; }
    }

    #region Init

    public MusicMatcher(string cachePath, TimeSpan maxCacheDuration)
    {
      _cachePath = cachePath;
      _matchesSettingsFile = Path.Combine(cachePath, "MusicMatches.xml");
      _maxCacheDuration = maxCacheDuration;

      _artistMatcher = new SimpleNameMatcher(Path.Combine(cachePath, "ArtistMatches.xml"));
      _composerMatcher = new SimpleNameMatcher(Path.Combine(cachePath, "ComposerMatches.xml"));
      _labelMatcher = new SimpleNameMatcher(Path.Combine(cachePath, "LabelMatches.xml"));
      _configFile = Path.Combine(cachePath, "MusicConfig.xml");

      Init();
    }

    public override bool Init()
    {
      if (_wrapper != null)
        return true;

      if (!base.Init())
        return false;

      return InitWrapper();
    }

    private void LoadConfig()
    {
      _config = Settings.Load<MusicMatcherSettings>(_configFile);
      if (_config == null)
        _config = new MusicMatcherSettings();
    }

    private void SaveConfig()
    {
      Settings.Save(_configFile, _config);
    }

    public abstract bool InitWrapper();

    #endregion

    #region Constants

    protected override string MatchesSettingsFile
    {
      get { return _matchesSettingsFile; }
    }

    #endregion

    #region Fields

    private DateTime _memoryCacheInvalidated = DateTime.MinValue;
    private ConcurrentDictionary<string, TrackInfo> _memoryCache = new ConcurrentDictionary<string, TrackInfo>(StringComparer.OrdinalIgnoreCase);
    private MusicMatcherSettings _config = new MusicMatcherSettings();
    private string _cachePath;
    private string _matchesSettingsFile;
    private string _configFile;
    private TimeSpan _maxCacheDuration;

    private SimpleNameMatcher _artistMatcher;
    private SimpleNameMatcher _composerMatcher;
    private SimpleNameMatcher _labelMatcher;

    /// <summary>
    /// Contains the initialized MovieWrapper.
    /// </summary>
    protected ApiWrapper<TImg, TLang> _wrapper = null;

    #endregion

    #region External match storage

    public void StoreArtistMatch(PersonInfo person)
    {
      string id;
      if (GetPersonId(person, out id))
        _artistMatcher.StoreNameMatch(id, person.Name, person.Name);
    }

    public void StoreComposerMatch(PersonInfo person)
    {
      string id;
      if (GetPersonId(person, out id))
        _composerMatcher.StoreNameMatch(id, person.Name, person.Name);
    }

    public void StoreMusicLabelMatch(CompanyInfo company)
    {
      string id;
      if (GetCompanyId(company, out id))
        _labelMatcher.StoreNameMatch(id, company.Name, company.Name);
    }

    #endregion

    #region Metadata updaters

    /// <summary>
    /// Tries to lookup the music track online and downloads images.
    /// </summary>
    /// <param name="trackInfo">Track to check</param>
    /// <returns><c>true</c> if successful</returns>
    public virtual bool FindAndUpdateTrack(TrackInfo trackInfo, bool forceQuickMode)
    {
      try
      {
        // Try online lookup
        if (!Init())
          return false;

        TrackInfo trackMatch = null;
        string trackId = null;
        bool matchFound = false;
        TLang language = FindBestMatchingLanguage(trackInfo);
        if (GetTrackId(trackInfo, out trackId))
        {
          // Prefer memory cache
          CheckCacheAndRefresh();
          if (_memoryCache.TryGetValue(trackId, out trackMatch))
            matchFound = true;
        }

        if (!matchFound)
        {
          // Load cache or create new list
          List<TrackMatch> matches = _storage.GetMatches();

          // Use cached values before doing online query
          TrackMatch match = matches.Find(m =>
            (string.Equals(m.ItemName, GetUniqueTrackName(trackInfo), StringComparison.OrdinalIgnoreCase) || string.Equals(m.TrackName, trackInfo.TrackName, StringComparison.OrdinalIgnoreCase)) &&
            (!string.IsNullOrEmpty(m.ArtistName) && trackInfo.Artists.Count > 0 ? trackInfo.Artists[0].Name.Equals(m.ArtistName, StringComparison.OrdinalIgnoreCase) : true) &&
            (!string.IsNullOrEmpty(m.AlbumName) && !string.IsNullOrEmpty(trackInfo.Album) ? trackInfo.Album.Equals(m.AlbumName, StringComparison.OrdinalIgnoreCase) : true) &&
            ((trackInfo.TrackNum > 0 && m.TrackNum > 0 && int.Equals(m.TrackNum, trackInfo.TrackNum) || trackInfo.TrackNum <= 0 || m.TrackNum <= 0)));
          ServiceRegistration.Get<ILogger>().Debug(GetType().Name + ": Try to lookup movie \"{0}\" from cache: {1}", trackInfo, match != null && !string.IsNullOrEmpty(match.Id));

          trackMatch = CloneProperties(trackInfo);
          if (match != null && match.Id != null)
          {
            if (SetTrackId(trackMatch, match.Id))
            {
              //If Id was found in cache the online track info is probably also in the cache
              if (_wrapper.UpdateFromOnlineMusicTrack(trackMatch, language, true))
                matchFound = true;
            }
          }

          if (!matchFound && !forceQuickMode)
          {
            //Try to update track information from online source if online Ids are present
            if (!_wrapper.UpdateFromOnlineMusicTrack(trackMatch, language, false))
            {
              //Search for the track online and update the Ids if a match is found
              if (_wrapper.SearchTrackUniqueAndUpdate(trackMatch, language))
              {
                //Ids were updated now try to update track information from online source
                if (_wrapper.UpdateFromOnlineMusicTrack(trackMatch, language, false))
                  matchFound = true;
              }
            }
            else
            {
              matchFound = true;
            }
          }
        }

        //Always save match even if none to avoid retries
        StoreTrackMatch(trackInfo, trackMatch);

        if (matchFound)
        {
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AudioDbId, trackMatch.AudioDbId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.MusicBrainzId, trackMatch.MusicBrainzId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.IsrcId, trackMatch.IsrcId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.LyricId, trackMatch.LyricId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.MusicIpId, trackMatch.MusicIpId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.MvDbId, trackMatch.MvDbId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AlbumAudioDbId, trackMatch.AlbumAudioDbId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AlbumCdDdId, trackMatch.AlbumCdDdId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AlbumMusicBrainzDiscId, trackMatch.AlbumMusicBrainzDiscId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AlbumMusicBrainzGroupId, trackMatch.AlbumMusicBrainzGroupId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AlbumMusicBrainzId, trackMatch.AlbumMusicBrainzId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AlbumAmazonId, trackMatch.AlbumAmazonId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AlbumItunesId, trackMatch.AlbumItunesId);
          MetadataUpdater.SetOrUpdateId(ref trackInfo.AlbumUpcEanId, trackMatch.AlbumUpcEanId);

          MetadataUpdater.SetOrUpdateString(ref trackInfo.TrackName, trackMatch.TrackName);
          MetadataUpdater.SetOrUpdateString(ref trackInfo.TrackLyrics, trackMatch.TrackLyrics);
          MetadataUpdater.SetOrUpdateString(ref trackInfo.Album, trackMatch.Album);

          MetadataUpdater.SetOrUpdateValue(ref trackInfo.DiscNum, trackMatch.DiscNum);
          MetadataUpdater.SetOrUpdateValue(ref trackInfo.ReleaseDate, trackMatch.ReleaseDate);
          MetadataUpdater.SetOrUpdateValue(ref trackInfo.TotalDiscs, trackMatch.TotalDiscs);
          MetadataUpdater.SetOrUpdateValue(ref trackInfo.TotalTracks, trackMatch.TotalTracks);
          MetadataUpdater.SetOrUpdateValue(ref trackInfo.TrackNum, trackMatch.TrackNum);

          MetadataUpdater.SetOrUpdateRatings(ref trackInfo.Rating, trackMatch.Rating);

          MetadataUpdater.SetOrUpdateList(trackInfo.AlbumArtists, trackMatch.AlbumArtists, true);
          MetadataUpdater.SetOrUpdateList(trackInfo.Artists, trackMatch.Artists, true);
          MetadataUpdater.SetOrUpdateList(trackInfo.Composers, trackMatch.Composers, true);
          MetadataUpdater.SetOrUpdateList(trackInfo.Genres, trackMatch.Genres, true);
          MetadataUpdater.SetOrUpdateList(trackInfo.MusicLabels, trackMatch.MusicLabels, true);

          //Store person matches
          foreach (PersonInfo person in trackInfo.AlbumArtists)
          {
            string id;
            if (GetPersonId(person, out id))
              _artistMatcher.StoreNameMatch(id, person.Name, person.Name);
          }
          foreach (PersonInfo person in trackInfo.Artists)
          {
            string id;
            if (GetPersonId(person, out id))
              _artistMatcher.StoreNameMatch(id, person.Name, person.Name);
          }
          foreach (PersonInfo person in trackInfo.Composers)
          {
            string id;
            if (GetPersonId(person, out id))
              _composerMatcher.StoreNameMatch(id, person.Name, person.Name);
          }

          //Store company matches
          foreach (CompanyInfo company in trackInfo.MusicLabels)
          {
            string id;
            if (GetCompanyId(company, out id))
              _labelMatcher.StoreNameMatch(id, company.Name, company.Name);
          }

          MetadataUpdater.SetOrUpdateValue(ref trackMatch.Thumbnail, trackMatch.Thumbnail);
          if (trackInfo.Thumbnail == null)
          {
            List<string> thumbs = GetFanArtFiles(trackInfo, FanArtMediaTypes.Movie, FanArtTypes.Poster);
            if (thumbs.Count > 0)
              trackInfo.Thumbnail = File.ReadAllBytes(thumbs[0]);
          }

          if (GetTrackId(trackInfo, out trackId))
          {
            _memoryCache.TryAdd(trackId, trackInfo);
            ScheduleDownload(trackId);
          }

          return true;
        }

        return false;
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + ": Exception while processing movie {0}", ex, trackInfo.ToString());
        return false;
      }
    }

    public virtual bool UpdateTrackPersons(TrackInfo trackInfo, string occupation, bool forceQuickMode)
    {
      try
      {
        // Try online lookup
        if (!Init())
          return false;

        TLang language = FindBestMatchingLanguage(trackInfo);
        bool updated = false;
        TrackInfo trackMatch = CloneProperties(trackInfo);
        List<PersonInfo> persons = new List<PersonInfo>();
        if (occupation == PersonAspect.OCCUPATION_ARTIST)
        {
          persons = trackMatch.Artists;
          foreach (PersonInfo person in persons)
          {
            string id;
            if (_artistMatcher.GetNameMatch(person.Name, out id))
            {
              if (SetPersonId(person, id))
                updated = true;
            }
          }
        }
        else if (occupation == PersonAspect.OCCUPATION_COMPOSER)
        {
          persons = trackMatch.Composers;
          foreach (PersonInfo person in persons)
          {
            string id;
            if (_composerMatcher.GetNameMatch(person.Name, out id))
            {
              if (SetPersonId(person, id))
                updated = true;
            }
          }
        }
        foreach (PersonInfo person in persons)
        {
          //Try updating from cache
          if (!_wrapper.UpdateFromOnlineMusicTrackPerson(trackMatch, person, language, true))
          {
            if (!forceQuickMode)
            {
              //Try to update person information from online source if online Ids are present
              if (!_wrapper.UpdateFromOnlineMusicTrackPerson(trackMatch, person, language, false))
              {
                //Search for the person online and update the Ids if a match is found
                if (_wrapper.SearchPersonUniqueAndUpdate(person, language))
                {
                  //Ids were updated now try to fetch the online person info
                  if (_wrapper.UpdateFromOnlineMusicTrackPerson(trackMatch, person, language, false))
                    updated = true;
                }
              }
              else
              {
                updated = true;
              }
            }
          }
          else
          {
            updated = true;
          }
        }

        if (updated == false && occupation == PersonAspect.OCCUPATION_ARTIST)
        {
          //Try to update artist based on album information
          AlbumInfo album = trackMatch.CloneBasicInstance<AlbumInfo>();
          album.Artists = trackMatch.Artists;
          if (UpdateAlbumPersons(album, occupation, forceQuickMode))
            updated = true;
        }

        if (updated)
        {
          if (occupation == PersonAspect.OCCUPATION_ARTIST)
            MetadataUpdater.SetOrUpdateList(trackInfo.Artists, trackMatch.Artists, false);
          else if (occupation == PersonAspect.OCCUPATION_COMPOSER)
            MetadataUpdater.SetOrUpdateList(trackInfo.Composers, trackMatch.Composers, false);
        }

        List<string> thumbs = new List<string>();
        if (occupation == PersonAspect.OCCUPATION_ARTIST)
        {
          foreach (PersonInfo person in trackInfo.Artists)
          {
            string id;
            if (GetPersonId(person, out id))
              _artistMatcher.StoreNameMatch(id, person.Name, person.Name);

            if (person.Thumbnail == null)
            {
              thumbs = GetFanArtFiles(person, FanArtMediaTypes.Artist, FanArtTypes.Thumbnail);
              if (thumbs.Count > 0)
                person.Thumbnail = File.ReadAllBytes(thumbs[0]);
            }
          }
        }
        else if (occupation == PersonAspect.OCCUPATION_COMPOSER)
        {
          foreach (PersonInfo person in trackInfo.Composers)
          {
            string id;
            if (GetPersonId(person, out id))
              _composerMatcher.StoreNameMatch(id, person.Name, person.Name);

            if (person.Thumbnail == null)
            {
              thumbs = GetFanArtFiles(person, FanArtMediaTypes.Writer, FanArtTypes.Thumbnail);
              if (thumbs.Count > 0)
                person.Thumbnail = File.ReadAllBytes(thumbs[0]);
            }
          }
        }

        return updated;
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + ": Exception while processing persons {0}", ex, trackInfo.ToString());
        return false;
      }
    }

    public virtual bool UpdateAlbumPersons(AlbumInfo albumInfo, string occupation, bool forceQuickMode)
    {
      try
      {
        // Try online lookup
        if (!Init())
          return false;

        TLang language = FindBestMatchingLanguage(albumInfo);
        bool updated = false;
        AlbumInfo albumMatch = CloneProperties(albumInfo);
        List<PersonInfo> persons = new List<PersonInfo>();
        if (occupation == PersonAspect.OCCUPATION_ARTIST)
        {
          persons = albumMatch.Artists;
          foreach (PersonInfo person in persons)
          {
            string id;
            if (_artistMatcher.GetNameMatch(person.Name, out id))
            {
              if (SetPersonId(person, id))
                updated = true;
            }
          }
        }
        foreach (PersonInfo person in persons)
        {
          //Try updating from cache
          if (!_wrapper.UpdateFromOnlineMusicTrackAlbumPerson(albumMatch, person, language, true))
          {
            if (!forceQuickMode)
            {
              //Try to update person information from online source if online Ids are present
              if (!_wrapper.UpdateFromOnlineMusicTrackAlbumPerson(albumMatch, person, language, false))
              {
                //Search for the person online and update the Ids if a match is found
                if (_wrapper.SearchPersonUniqueAndUpdate(person, language))
                {
                  //Ids were updated now try to fetch the online person info
                  if (_wrapper.UpdateFromOnlineMusicTrackAlbumPerson(albumMatch, person, language, false))
                    updated = true;
                }
              }
              else
              {
                updated = true;
              }
            }
          }
          else
          {
            updated = true;
          }
        }

        if (updated)
        {
          if (occupation == PersonAspect.OCCUPATION_ARTIST)
            MetadataUpdater.SetOrUpdateList(albumInfo.Artists, albumMatch.Artists, false);
        }

        List<string> thumbs = new List<string>();
        if (occupation == PersonAspect.OCCUPATION_ARTIST)
        {
          foreach (PersonInfo person in albumInfo.Artists)
          {
            string id;
            if (GetPersonId(person, out id))
              _artistMatcher.StoreNameMatch(id, person.Name, person.Name);

            if (person.Thumbnail == null)
            {
              thumbs = GetFanArtFiles(person, FanArtMediaTypes.Artist, FanArtTypes.Thumbnail);
              if (thumbs.Count > 0)
                person.Thumbnail = File.ReadAllBytes(thumbs[0]);
            }
          }
        }

        return updated;
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + ": Exception while processing persons {0}", ex, albumInfo.ToString());
        return false;
      }
    }

    public virtual bool UpdateAlbumCompanies(AlbumInfo albumInfo, string companyType, bool forceQuickMode)
    {
      try
      {
        // Try online lookup
        if (!Init())
          return false;

        TLang language = FindBestMatchingLanguage(albumInfo);
        bool updated = false;
        AlbumInfo albumMatch = CloneProperties(albumInfo);
        List<CompanyInfo> companies = new List<CompanyInfo>();
        if (companyType == CompanyAspect.COMPANY_MUSIC_LABEL)
        {
          companies = albumMatch.MusicLabels;
          foreach (CompanyInfo company in companies)
          {
            string id;
            if (_labelMatcher.GetNameMatch(company.Name, out id))
            {
              if (SetCompanyId(company, id))
                updated = true;
            }
          }
        }
        foreach (CompanyInfo company in companies)
        {
          //Try updating from cache
          if (!_wrapper.UpdateFromOnlineMusicTrackAlbumCompany(albumMatch, company, language, true))
          {
            if (!forceQuickMode)
            {
              //Try to update company information from online source if online Ids are present
              if (!_wrapper.UpdateFromOnlineMusicTrackAlbumCompany(albumMatch, company, language, false))
              {
                //Search for the company online and update the Ids if a match is found
                if (_wrapper.SearchCompanyUniqueAndUpdate(company, language))
                {
                  //Ids were updated now try to fetch the online company info
                  if (_wrapper.UpdateFromOnlineMusicTrackAlbumCompany(albumMatch, company, language, false))
                    updated = true;
                }
              }
              else
              {
                updated = true;
              }
            }
          }
          else
          {
            updated = true;
          }
        }

        if (updated)
        {
          if (companyType == CompanyAspect.COMPANY_MUSIC_LABEL)
            MetadataUpdater.SetOrUpdateList(albumInfo.MusicLabels, albumMatch.MusicLabels, false);
        }

        List<string> thumbs = new List<string>();
        if (companyType == CompanyAspect.COMPANY_MUSIC_LABEL)
        {
          foreach (CompanyInfo company in albumInfo.MusicLabels)
          {
            string id;
            if (GetCompanyId(company, out id))
              _labelMatcher.StoreNameMatch(id, company.Name, company.Name);

            if (company.Thumbnail == null)
            {
              thumbs = GetFanArtFiles(company, FanArtMediaTypes.Company, FanArtTypes.Logo);
              if (thumbs.Count > 0)
                company.Thumbnail = File.ReadAllBytes(thumbs[0]);
            }
          }
        }

        return updated;
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + ": Exception while processing companies {0}", ex, albumInfo.ToString());
        return false;
      }
    }

    public virtual bool UpdateAlbum(AlbumInfo albumInfo, bool updateTrackList, bool forceQuickMode)
    {
      try
      {
        // Try online lookup
        if (!Init())
          return false;

        TLang language = FindBestMatchingLanguage(albumInfo);
        bool updated = false;
        AlbumInfo albumMatch = CloneProperties(albumInfo);
        albumMatch.Tracks.Clear();
        //Try updating from cache
        if (!_wrapper.UpdateFromOnlineMusicTrackAlbum(albumMatch, language, true))
        {
          if (!forceQuickMode)
          {
            //Try to update company information from online source if online Ids are present
            if (!_wrapper.UpdateFromOnlineMusicTrackAlbum(albumMatch, language, false))
            {
              //Search for the company online and update the Ids if a match is found
              if (_wrapper.SearchTrackAlbumUniqueAndUpdate(albumMatch, language))
              {
                //Ids were updated now try to fetch the online company info
                if (_wrapper.UpdateFromOnlineMusicTrackAlbum(albumMatch, language, false))
                  updated = true;
              }
            }
            else
            {
              updated = true;
            }
          }
        }
        else
        {
          updated = true;
        }

        if (updated)
        {
          MetadataUpdater.SetOrUpdateId(ref albumInfo.AudioDbId, albumMatch.AudioDbId);
          MetadataUpdater.SetOrUpdateId(ref albumInfo.CdDdId, albumMatch.CdDdId);
          MetadataUpdater.SetOrUpdateId(ref albumInfo.MusicBrainzDiscId, albumMatch.MusicBrainzDiscId);
          MetadataUpdater.SetOrUpdateId(ref albumInfo.MusicBrainzGroupId, albumMatch.MusicBrainzGroupId);
          MetadataUpdater.SetOrUpdateId(ref albumInfo.MusicBrainzId, albumMatch.MusicBrainzId);
          MetadataUpdater.SetOrUpdateId(ref albumInfo.AmazonId, albumMatch.AmazonId);
          MetadataUpdater.SetOrUpdateId(ref albumInfo.ItunesId, albumMatch.ItunesId);
          MetadataUpdater.SetOrUpdateId(ref albumInfo.UpcEanId, albumMatch.UpcEanId);

          MetadataUpdater.SetOrUpdateString(ref albumInfo.Album, albumMatch.Album);
          MetadataUpdater.SetOrUpdateString(ref albumInfo.Description, albumMatch.Description);

          if (albumInfo.TotalTracks < albumMatch.TotalTracks)
            MetadataUpdater.SetOrUpdateValue(ref albumInfo.TotalTracks, albumMatch.TotalTracks);

          MetadataUpdater.SetOrUpdateValue(ref albumInfo.Compilation, albumMatch.Compilation);
          MetadataUpdater.SetOrUpdateValue(ref albumInfo.DiscNum, albumMatch.DiscNum);
          MetadataUpdater.SetOrUpdateValue(ref albumInfo.ReleaseDate, albumMatch.ReleaseDate);
          MetadataUpdater.SetOrUpdateValue(ref albumInfo.Sales, albumMatch.Sales);
          MetadataUpdater.SetOrUpdateValue(ref albumInfo.TotalDiscs, albumMatch.TotalDiscs);

          MetadataUpdater.SetOrUpdateRatings(ref albumInfo.Rating, albumMatch.Rating);

          MetadataUpdater.SetOrUpdateList(albumInfo.Artists, albumMatch.Artists, true);
          MetadataUpdater.SetOrUpdateList(albumInfo.Awards, albumMatch.Awards, true);
          MetadataUpdater.SetOrUpdateList(albumInfo.Genres, albumMatch.Genres, true);
          MetadataUpdater.SetOrUpdateList(albumInfo.MusicLabels, albumMatch.MusicLabels, true);
          if (updateTrackList) //Comparing all tracks can be quite time consuming
            MetadataUpdater.SetOrUpdateList(albumInfo.Tracks, albumMatch.Tracks, true);

          //Store person matches
          foreach (PersonInfo person in albumInfo.Artists)
          {
            string id;
            if (GetPersonId(person, out id))
              _artistMatcher.StoreNameMatch(id, person.Name, person.Name);
          }

          //Store company matches
          foreach (CompanyInfo company in albumInfo.MusicLabels)
          {
            string id;
            if (GetCompanyId(company, out id))
              _labelMatcher.StoreNameMatch(id, company.Name, company.Name);
          }

          MetadataUpdater.SetOrUpdateValue(ref albumInfo.Thumbnail, albumMatch.Thumbnail);
        }

        if (albumInfo.Thumbnail == null)
        {
          List<string> thumbs = GetFanArtFiles(albumInfo, FanArtMediaTypes.Album, FanArtTypes.Cover);
          if (thumbs.Count > 0)
            albumInfo.Thumbnail = File.ReadAllBytes(thumbs[0]);
        }

        return updated;
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + ": Exception while processing collection {0}", ex, albumInfo.ToString());
        return false;
      }
    }

    public virtual bool FindAndUpdateTrackPerson(TrackInfo trackInfo, PersonInfo person, bool forceQuickMode)
    {
      try
      {
        // Try online lookup
        if (!Init())
          return false;

        TLang language = FindBestMatchingLanguage(trackInfo);
        string id;
        bool updated = false;
        if (_artistMatcher.GetNameMatch(person.Name, out id))
        {
          if (SetPersonId(person, id))
            updated = true;
        }

        //Try updating from cache
        if (!_wrapper.UpdateFromOnlineMusicTrackPerson(trackInfo, person, language, true))
        {
          if (!forceQuickMode)
          {
            //Try to update person information from online source if online Ids are present
            if (!_wrapper.UpdateFromOnlineMusicTrackPerson(trackInfo, person, language, false))
            {
              //Search for the person online and update the Ids if a match is found
              if (_wrapper.SearchPersonUniqueAndUpdate(person, language))
              {
                //Ids were updated now try to fetch the online person info
                if (_wrapper.UpdateFromOnlineMusicTrackPerson(trackInfo, person, language, false))
                  updated = true;
              }
            }
            else
            {
              updated = true;
            }
          }
        }
        else
        {
          updated = true;
        }

        return updated;
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + ": Exception while processing person {0}", ex, person.ToString());
        return false;
      }
    }

    #endregion

    #region Metadata update helpers

    private T CloneProperties<T>(T obj)
    {
      if (obj == null)
        return default(T);
      Type type = obj.GetType();

      if (type.IsValueType || type == typeof(string))
      {
        return obj;
      }
      else if (type.IsArray)
      {
        Type elementType = obj.GetType().GetElementType();
        var array = obj as Array;
        Array arrayCopy = Array.CreateInstance(elementType, array.Length);
        for (int i = 0; i < array.Length; i++)
        {
          arrayCopy.SetValue(CloneProperties(array.GetValue(i)), i);
        }
        return (T)Convert.ChangeType(arrayCopy, obj.GetType());
      }
      else if (type.IsClass)
      {
        T newInstance = (T)Activator.CreateInstance(obj.GetType());
        FieldInfo[] fields = type.GetFields(BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (FieldInfo field in fields)
        {
          object fieldValue = field.GetValue(obj);
          if (fieldValue == null)
            continue;
          field.SetValue(newInstance, CloneProperties(fieldValue));
        }
        return newInstance;
      }
      return default(T);
    }

    private string GetUniqueTrackName(TrackInfo trackInfo)
    {
      return string.Format("{0}: {1} - {2} [{3}]",
        !string.IsNullOrEmpty(trackInfo.Album) ? trackInfo.Album : "?",
        trackInfo.TrackNum > 0 ? trackInfo.TrackNum : 0,
        !string.IsNullOrEmpty(trackInfo.TrackName) ? trackInfo.TrackName : "?",
        trackInfo.Artists.Count > 0 ? trackInfo.Artists[0].Name : "?");
    }

    private void StoreTrackMatch(TrackInfo trackSearch, TrackInfo trackMatch)
    {
      string idValue = null;
      if (trackMatch == null || !GetTrackId(trackSearch, out idValue) || string.IsNullOrEmpty(trackMatch.TrackName))
      {
        //No match was found. Store search to avoid online search again
        _storage.TryAddMatch(new TrackMatch()
        {
          ItemName = GetUniqueTrackName(trackSearch),
        });
        return;
      }

      var onlineMatch = new TrackMatch
      {
        Id = idValue,
        ItemName = GetUniqueTrackName(trackSearch),
        ArtistName = trackMatch.Artists.Count > 0 ? trackMatch.Artists[0].Name : "",
        TrackName = !string.IsNullOrEmpty(trackMatch.TrackName) ? trackMatch.TrackName : "",
        AlbumName = !string.IsNullOrEmpty(trackMatch.Album) ? trackMatch.Album : "",
        TrackNum = trackMatch.TrackNum > 0 ? trackMatch.TrackNum : 0
      };
      _storage.TryAddMatch(onlineMatch);
    }

    protected virtual TLang FindBestMatchingLanguage(TrackInfo trackInfo)
    {
      if (typeof(TLang) == typeof(string))
      {
        CultureInfo mpLocal = ServiceRegistration.Get<ILocalization>().CurrentCulture;
        // If we don't have movie languages available, or the MP2 setting language is available, prefer it.
        if (trackInfo.Languages.Count == 0 || trackInfo.Languages.Contains(mpLocal.TwoLetterISOLanguageName))
          return (TLang)Convert.ChangeType(mpLocal.TwoLetterISOLanguageName, typeof(TLang));

        // If there is only one language available, use this one.
        if (trackInfo.Languages.Count == 1)
          return (TLang)Convert.ChangeType(trackInfo.Languages[0], typeof(TLang));
      }
      // If there are multiple languages, that are different to MP2 setting, we cannot guess which one is the "best".
      // By returning null we allow fallback to the default language of the online source (en).
      return default(TLang);
    }

    protected virtual TLang FindBestMatchingLanguage(AlbumInfo albumInfo)
    {
      if (typeof(TLang) == typeof(string))
      {
        CultureInfo mpLocal = ServiceRegistration.Get<ILocalization>().CurrentCulture;
        // If we don't have movie languages available, or the MP2 setting language is available, prefer it.
        if (albumInfo.Languages.Count == 0 || albumInfo.Languages.Contains(mpLocal.TwoLetterISOLanguageName))
          return (TLang)Convert.ChangeType(mpLocal.TwoLetterISOLanguageName, typeof(TLang));

        // If there is only one language available, use this one.
        if (albumInfo.Languages.Count == 1)
          return (TLang)Convert.ChangeType(albumInfo.Languages[0], typeof(TLang));
      }
      // If there are multiple languages, that are different to MP2 setting, we cannot guess which one is the "best".
      // By returning null we allow fallback to the default language of the online source (en).
      return default(TLang);
    }

    #endregion

    #region Ids

    protected abstract bool GetTrackId(TrackInfo track, out string id);

    protected abstract bool SetTrackId(TrackInfo track, string id);

    protected virtual bool GetTrackAlbumId(AlbumInfo album, out string id)
    {
      id = null;
      return false;
    }

    protected virtual bool GetPersonId(PersonInfo person, out string id)
    {
      id = null;
      return false;
    }

    protected virtual bool SetPersonId(PersonInfo person, string id)
    {
      return false;
    }

    protected virtual bool GetCompanyId(CompanyInfo company, out string id)
    {
      id = null;
      return false;
    }

    protected virtual bool SetCompanyId(CompanyInfo company, string id)
    {
      return false;
    }

    #endregion

    #region Caching

    /// <summary>
    /// Check if the memory cache should be cleared and starts an online update of (file-) cached series information.
    /// </summary>
    private void CheckCacheAndRefresh()
    {
      if (DateTime.Now - _memoryCacheInvalidated <= _maxCacheDuration)
        return;
      _memoryCache.Clear();
      _memoryCacheInvalidated = DateTime.Now;

      RefreshCache();
    }

    protected virtual void RefreshCache()
    {
      string dateFormat = "MMddyyyyHHmm";
      if (string.IsNullOrEmpty(_config.LastRefresh))
        _config.LastRefresh = DateTime.Now.ToString(dateFormat);

      DateTime lastRefresh = DateTime.ParseExact(_config.LastRefresh, dateFormat, CultureInfo.InvariantCulture);

      if (DateTime.Now - lastRefresh <= _maxCacheDuration)
        return;

      IThreadPool threadPool = ServiceRegistration.Get<IThreadPool>(false);
      if (threadPool != null)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + ": Refreshing local cache");
        threadPool.Add(() =>
        {
          if (_wrapper != null)
            _wrapper.RefreshCache(lastRefresh);
        });
      }

      _config.LastRefresh = DateTime.Now.ToString(dateFormat, CultureInfo.InvariantCulture);
      SaveConfig();
    }

    #endregion

    #region FanArt

    public virtual List<string> GetFanArtFiles<T>(T infoObject, string scope, string type)
    {
      List<string> fanartFiles = new List<string>();
      string path = null;
      string id;
      if (scope == FanArtMediaTypes.Album || scope == FanArtMediaTypes.Audio)
      {
        TrackInfo track = infoObject as TrackInfo;
        AlbumInfo album = infoObject as AlbumInfo;
        if (album == null && track != null)
        {
          album = track.CloneBasicInstance<AlbumInfo>();
        }
        if (album != null && GetTrackAlbumId(album, out id))
        {
          path = Path.Combine(_cachePath, id, string.Format(@"{0}\{1}\", scope, type));
        }
      }
      else if (scope == FanArtMediaTypes.Artist || scope == FanArtMediaTypes.Writer)
      {
        PersonInfo person = infoObject as PersonInfo;
        if (person != null && GetPersonId(person, out id))
        {
          path = Path.Combine(_cachePath, id, string.Format(@"{0}\{1}\", scope, type));
        }
      }
      else if (scope == FanArtMediaTypes.Company)
      {
        CompanyInfo company = infoObject as CompanyInfo;
        if (company != null && GetCompanyId(company, out id))
        {
          path = Path.Combine(_cachePath, id, string.Format(@"{0}\{1}\", scope, type));
        }
      }
      if (Directory.Exists(path))
      {
        fanartFiles.AddRange(Directory.GetFiles(path, "*.jpg"));
        while (fanartFiles.Count > MAX_FANART_IMAGES)
        {
          fanartFiles.RemoveAt(fanartFiles.Count - 1);
        }
      }
      return fanartFiles;
    }

    protected override void DownloadFanArt(string downloadId)
    {
      try
      {
        if (string.IsNullOrEmpty(downloadId))
          return;

        string trackId = downloadId;

        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Started for ID {0}", downloadId);

        TrackInfo trackInfo;
        if (!_memoryCache.TryGetValue(trackId, out trackInfo))
          return;

        if (!Init())
          return;

        TLang language = FindBestMatchingLanguage(trackInfo);
        ApiWrapperImageCollection<TImg> images;
        string scope = FanArtMediaTypes.Album;
        if (_wrapper.GetFanArt(trackInfo, language, scope, out images) == false)
        {
          ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Failed getting images for track ID {0}", downloadId);

          return;
        }

        if (images != null)
        {
          ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Downloading track images for ID {0}", downloadId);

          SaveFanArtImages(images.Id, images.Backdrops, scope, FanArtTypes.FanArt);
          SaveFanArtImages(images.Id, images.Posters, scope, FanArtTypes.Poster);
          SaveFanArtImages(images.Id, images.Banners, scope, FanArtTypes.Banner);
          SaveFanArtImages(images.Id, images.ClearArt, scope, FanArtTypes.ClearArt);
          SaveFanArtImages(images.Id, images.Covers, scope, FanArtTypes.Cover);
          SaveFanArtImages(images.Id, images.DiscArt, scope, FanArtTypes.DiscArt);
          SaveFanArtImages(images.Id, images.Logos, scope, FanArtTypes.Logo);
          SaveFanArtImages(images.Id, images.Thumbnails, scope, FanArtTypes.Thumbnail);
        }

        scope = FanArtMediaTypes.Artist;
        List<PersonInfo> persons = new List<PersonInfo>(trackInfo.Artists);
        persons = persons.Union(new List<PersonInfo>(trackInfo.AlbumArtists)).ToList();
        if (persons != null && persons.Count > 0)
        {
          ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Downloading artist images for ID {0}", downloadId);
          foreach (PersonInfo person in persons)
          {
            if (_wrapper.GetFanArt(person, language, scope, out images) == false)
            {
              if (images != null)
              {
                SaveFanArtImages(images.Id, images.Backdrops, scope, FanArtTypes.FanArt);
                SaveFanArtImages(images.Id, images.Posters, scope, FanArtTypes.Poster);
                SaveFanArtImages(images.Id, images.Banners, scope, FanArtTypes.Banner);
                SaveFanArtImages(images.Id, images.ClearArt, scope, FanArtTypes.ClearArt);
                SaveFanArtImages(images.Id, images.Covers, scope, FanArtTypes.Cover);
                SaveFanArtImages(images.Id, images.DiscArt, scope, FanArtTypes.DiscArt);
                SaveFanArtImages(images.Id, images.Logos, scope, FanArtTypes.Logo);
                SaveFanArtImages(images.Id, images.Thumbnails, scope, FanArtTypes.Thumbnail);
              }
            }
          }
        }

        scope = FanArtMediaTypes.Writer;
        persons = new List<PersonInfo>(trackInfo.Composers);
        if (persons != null && persons.Count > 0)
        {
          ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Downloading composer images for ID {0}", downloadId);
          foreach (PersonInfo person in persons)
          {
            if (_wrapper.GetFanArt(person, language, scope, out images) == false)
            {
              if (images != null)
              {
                SaveFanArtImages(images.Id, images.Backdrops, scope, FanArtTypes.FanArt);
                SaveFanArtImages(images.Id, images.Posters, scope, FanArtTypes.Poster);
                SaveFanArtImages(images.Id, images.Banners, scope, FanArtTypes.Banner);
                SaveFanArtImages(images.Id, images.ClearArt, scope, FanArtTypes.ClearArt);
                SaveFanArtImages(images.Id, images.Covers, scope, FanArtTypes.Cover);
                SaveFanArtImages(images.Id, images.DiscArt, scope, FanArtTypes.DiscArt);
                SaveFanArtImages(images.Id, images.Logos, scope, FanArtTypes.Logo);
                SaveFanArtImages(images.Id, images.Thumbnails, scope, FanArtTypes.Thumbnail);
              }
            }
          }
        }

        scope = FanArtMediaTypes.MusicLabel;
        List<CompanyInfo> companies = new List<CompanyInfo>(trackInfo.MusicLabels);
        if (companies != null && companies.Count > 0)
        {
          ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Downloading label images for ID {0}", downloadId);
          foreach (CompanyInfo company in companies)
          {
            if (_wrapper.GetFanArt(company, language, scope, out images) == false)
            {
              if (images != null)
              {
                SaveFanArtImages(images.Id, images.Backdrops, scope, FanArtTypes.FanArt);
                SaveFanArtImages(images.Id, images.Posters, scope, FanArtTypes.Poster);
                SaveFanArtImages(images.Id, images.Banners, scope, FanArtTypes.Banner);
                SaveFanArtImages(images.Id, images.ClearArt, scope, FanArtTypes.ClearArt);
                SaveFanArtImages(images.Id, images.Covers, scope, FanArtTypes.Cover);
                SaveFanArtImages(images.Id, images.DiscArt, scope, FanArtTypes.DiscArt);
                SaveFanArtImages(images.Id, images.Logos, scope, FanArtTypes.Logo);
                SaveFanArtImages(images.Id, images.Thumbnails, scope, FanArtTypes.Thumbnail);
              }
            }
          }
        }

        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Finished saving images for ID {0}", downloadId);

        // Remember we are finished
        FinishDownloadFanArt(downloadId);
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Exception downloading images for ID {0}", ex, downloadId);
      }
    }

    protected virtual bool VerifyFanArtImage(TImg image)
    {
      return false;
    }

    protected virtual int SaveFanArtImages(string id, IEnumerable<TImg> images, string scope, string type)
    {
      try
      {
        if (images == null)
          return 0;

        int idx = 0;
        foreach (TImg img in images)
        {
          if (!VerifyFanArtImage(img))
            continue;
          if (idx >= MAX_FANART_IMAGES)
            break;
          if (_wrapper.DownloadFanArt(id, img, scope, type))
            idx++;
        }
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + @" Download: Saved {0} {1}\{2}", idx, scope, type);
        return idx;
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Debug(GetType().Name + " Download: Exception downloading images for ID {0}", ex, id);
        return 0;
      }
    }

    #endregion
  }
}
