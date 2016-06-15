#region Copyright (C) 2007-2014 Team MediaPortal

/*
    Copyright (C) 2007-2014 Team MediaPortal
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
using System.Collections.Generic;
using System.Globalization;
using MediaPortal.Common;
using MediaPortal.Common.Localization;
using MediaPortal.Common.Logging;
using MediaPortal.Common.MediaManagement.Helpers;
using MediaPortal.Common.PathManager;
using MediaPortal.Extensions.OnlineLibraries.Libraries.MusicBrainzV2.Data;
using MediaPortal.Extensions.OnlineLibraries.MusicBrainz;

namespace MediaPortal.Extensions.OnlineLibraries
{
  public class MusicBrainzMatcher : MusicMatcher<TrackImage, string>
  {
    #region Static instance

    public static MusicBrainzMatcher Instance
    {
      get { return ServiceRegistration.Get<MusicBrainzMatcher>(); }
    }

    #endregion

    #region Constants

    public static string CACHE_PATH = ServiceRegistration.Get<IPathManager>().GetPath(@"<DATA>\MusicBrainz\");
    protected static TimeSpan MAX_MEMCACHE_DURATION = TimeSpan.FromHours(12);

    #endregion

    #region Init

    public MusicBrainzMatcher() : 
      base(CACHE_PATH, MAX_MEMCACHE_DURATION)
    {
    }

    public override bool InitWrapper()
    {
      try
      {
        MusicBrainzWrapper wrapper = new MusicBrainzWrapper();
        // Try to lookup online content in the configured language
        CultureInfo currentCulture = ServiceRegistration.Get<ILocalization>().CurrentCulture;
        string lang = new RegionInfo(currentCulture.LCID).TwoLetterISORegionName;
        wrapper.SetPreferredLanguage(lang);
        if (wrapper.Init(CACHE_PATH))
        {
          _wrapper = wrapper;
          return true;
        }
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Error("MusicBrainzMatcher: Error initializing wrapper", ex);
      }
      return false;
    }

    #endregion

    #region Translators

    protected override bool GetTrackAlbumId(AlbumInfo album, out string id)
    {
      id = null;
      if (!string.IsNullOrEmpty(album.MusicBrainzId))
        id = album.MusicBrainzId;
      return id != null;
    }

    protected override bool GetTrackId(TrackInfo track, out string id)
    {
      id = null;
      if (!string.IsNullOrEmpty(track.MusicBrainzId))
       id = track.MusicBrainzId;
      return id != null;
    }

    protected override bool SetTrackId(TrackInfo track, string id)
    {
      if (!string.IsNullOrEmpty(id))
      {
        track.MusicBrainzId = id;
        return true;
      }
      return false;
    }

    protected override bool GetCompanyId(CompanyInfo company, out string id)
    {
      id = null;
      if (!string.IsNullOrEmpty(company.MusicBrainzId))
        id = company.MusicBrainzId;
      return id != null;
    }

    protected override bool GetPersonId(PersonInfo person, out string id)
    {
      id = null;
      if (!string.IsNullOrEmpty(person.MusicBrainzId))
        id = person.MusicBrainzId;
      return id != null;
    }

    #endregion

    #region FanArt

    protected override int SaveFanArtImages(string id, IEnumerable<TrackImage> images, string scope, string type)
    {
      if (images == null)
        return 0;

      string imgType = null;
      if (type == FanArtType.Covers)
        imgType = "Front";
      else if (type == FanArtType.DiscArt)
        imgType = "Medium";

      if (imgType == null)
        return 0;

      int idx = 0;
      foreach (TrackImage img in images)
      {
        if (idx >= MAX_FANART_IMAGES)
          break;

        foreach (string imageType in img.Types)
        {
          if (imageType.Equals(imgType, StringComparison.InvariantCultureIgnoreCase))
          {
            if (_wrapper.DownloadFanArt(id, img, scope, type))
              idx++;
            break;
          }
        }
      }
      ServiceRegistration.Get<ILogger>().Debug(@"MusicBrainzMatcher Download: Saved {0} {1}\{2}", idx, scope, type);
      return idx;
    }

    #endregion
  }
}