import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import * as commandNames from 'Commands/commandNames';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import createHandleActions from './Creators/createHandleActions';
import { set, update } from './baseActions';
import { executeCommandHelper } from './commandActions';

//
// Variables

export const section = 'musicImport';

let abortCurrentFetchRequest = null;

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  isSaving: false,
  saveError: null,
  error: null,
  items: [],
  importFolder: null
};

//
// Action Types

export const FETCH_MUSIC_IMPORT_ITEMS = 'musicImport/fetchMusicImportItems';
export const SAVE_MUSIC_IMPORT_ITEMS = 'musicImport/saveMusicImportItems';
export const DELETE_MUSIC_IMPORT_DUPLICATES = 'musicImport/deleteMusicImportDuplicates';
export const LOOKUP_MUSIC_IMPORT_RECORDING = 'musicImport/lookupMusicImportRecording';
export const UPDATE_MUSIC_IMPORT_ITEM = 'musicImport/updateMusicImportItem';
export const CLEAR_MUSIC_IMPORT = 'musicImport/clearMusicImport';
export const SET_MUSIC_IMPORT_FOLDER = 'musicImport/setMusicImportFolder';

//
// Action Creators

export const fetchMusicImportItems = createThunk(FETCH_MUSIC_IMPORT_ITEMS);
export const saveMusicImportItems = createThunk(SAVE_MUSIC_IMPORT_ITEMS);
export const deleteMusicImportDuplicates = createThunk(DELETE_MUSIC_IMPORT_DUPLICATES);
export const lookupMusicImportRecording = createThunk(LOOKUP_MUSIC_IMPORT_RECORDING);
export const updateMusicImportItem = createAction(UPDATE_MUSIC_IMPORT_ITEM);
export const clearMusicImport = createAction(CLEAR_MUSIC_IMPORT);
export const setMusicImportFolder = createAction(SET_MUSIC_IMPORT_FOLDER);

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_MUSIC_IMPORT_ITEMS]: function(getState, payload, dispatch) {
    if (abortCurrentFetchRequest) {
      abortCurrentFetchRequest();
      abortCurrentFetchRequest = null;
    }

    const { folder } = payload;

    if (!folder) {
      dispatch(set({ section, error: { message: 'No import folder configured.' } }));
      return;
    }

    dispatch(set({ section, isFetching: true }));

    const { request, abortRequest } = createAjaxRequest({
      url: '/manualimport',
      data: {
        folder,
        filterExistingFiles: false,
        replaceExistingFiles: false
      }
    });

    abortCurrentFetchRequest = abortRequest;

    request.done((data) => {
      dispatch(batchActions([
        update({ section, data }),
        set({
          section,
          isFetching: false,
          isPopulated: true,
          error: null,
          importFolder: folder
        })
      ]));
    });

    request.fail((xhr) => {
      if (xhr.aborted) {
        return;
      }

      dispatch(set({
        section,
        isFetching: false,
        isPopulated: false,
        error: xhr
      }));
    });
  },

  // Dispatches a ManualImportCommand with importMode=move so Lidarr
  // imports each file, moves it to the correct artist/album directory,
  // and renames it according to the configured naming settings.
  [SAVE_MUSIC_IMPORT_ITEMS]: function(getState, payload, dispatch) {
    const { ids } = payload;
    const items = getState()[section].items;

    const files = [];

    for (const id of ids) {
      const item = items.find((i) => i.id === id);

      if (!item || !item.artist || !item.album || !item.tracks || !item.tracks.length) {
        continue;
      }

      // artist.id is 0 (falsy) when the artist was matched via a remote (Skyhook)
      // lookup but is not yet in the Lidarr library.  Send artistId: 0 so the backend
      // routes the file through the EnsureArtistAdded / EnsureAlbumAdded pipeline in
      // ImportApprovedTracks, which adds the artist and album synchronously before
      // moving the file.  Album and track IDs are likewise omitted because they aren't
      // in the DB yet — the backend re-identifies the file to populate them.
      const isNewArtist = !item.artist.id;

      files.push({
        path: item.path,
        artistId: isNewArtist ? 0 : item.artist.id,
        albumId: isNewArtist ? 0 : item.album.id,
        albumReleaseId: isNewArtist ? 0 : item.albumReleaseId,
        trackIds: isNewArtist ? [] : item.tracks.map((t) => t.id).filter((id) => id != null),
        quality: item.quality,
        indexerFlags: item.indexerFlags || 0,
        disableReleaseSwitching: false
      });
    }

    if (!files.length) {
      return;
    }

    dispatch(set({ section, isSaving: true, saveError: null }));

    executeCommandHelper({
      name: commandNames.INTERACTIVE_IMPORT,
      files,
      importMode: 'move',
      replaceExistingFiles: false,
      deleteRejectedFiles: true,
      commandFinished: () => {
        dispatch(set({ section, isSaving: false, saveError: null }));

        // Re-scan the import folder so imported files disappear from the list
        const importFolder = getState()[section].importFolder;

        if (importFolder) {
          dispatch(fetchMusicImportItems({ folder: importFolder }));
        }
      }
    }, dispatch);
  },

  // Sends only the hasExistingFiles items through the import command with
  // replaceExistingFiles: false.  The backend's quality check will reject them
  // (existing >= new) and deleteRejectedFiles: true will remove the source files
  // from the import folder, cleaning up duplicates without touching the library.
  [DELETE_MUSIC_IMPORT_DUPLICATES]: function(getState, payload, dispatch) {
    const { ids } = payload;
    const items = getState()[section].items;

    const files = ids
      .map((id) => items.find((i) => i.id === id))
      .filter((item) => item && item.hasExistingFiles && item.artist && item.artist.id && item.album && item.album.id && item.albumReleaseId && item.tracks && item.tracks.length)
      .map((item) => ({
        path: item.path,
        artistId: item.artist.id || 0,
        albumId: item.album.id || 0,
        albumReleaseId: item.albumReleaseId || 0,
        trackIds: item.tracks.map((t) => t.id).filter((id) => id != null),
        quality: item.quality,
        indexerFlags: item.indexerFlags || 0,
        disableReleaseSwitching: false
      }));

    if (!files.length) {
      return;
    }

    dispatch(set({ section, isSaving: true, saveError: null }));

    executeCommandHelper({
      name: commandNames.INTERACTIVE_IMPORT,
      files,
      importMode: 'move',
      replaceExistingFiles: false,
      deleteRejectedFiles: true,
      commandFinished: () => {
        dispatch(set({ section, isSaving: false, saveError: null }));

        // Re-scan so deleted files disappear from the list
        const importFolder = getState()[section].importFolder;

        if (importFolder) {
          dispatch(fetchMusicImportItems({ folder: importFolder }));
        }
      }
    }, dispatch);
  },

  // Calls GET /manualimport/lookup?recordingId={mbid} — returns artist, album,
  // albumReleaseId, and track from the local database. On success the matching
  // item in state is patched so the row shows the found metadata.
  [LOOKUP_MUSIC_IMPORT_RECORDING]: function(getState, payload, dispatch) {
    const { itemId, recordingId } = payload;

    // Mark this row as searching
    dispatch(updateMusicImportItem({ id: itemId, isLookingUp: true, lookupError: null }));

    const { request } = createAjaxRequest({
      url: '/manualimport/lookup',
      data: { recordingId }
    });

    request.done((data) => {
      // data = { artist, album, albumReleaseId, track }
      dispatch(updateMusicImportItem({
        id: itemId,
        isLookingUp: false,
        lookupError: null,
        artist: data.artist,
        album: data.album,
        albumReleaseId: data.albumReleaseId,
        tracks: [data.track]
      }));
    });

    request.fail((xhr) => {
      const notFound = xhr.status === 404;
      dispatch(updateMusicImportItem({
        id: itemId,
        isLookingUp: false,
        lookupError: notFound ?
          'Recording ID not found in your library' :
          'Lookup failed — check the ID and try again'
      }));
    });
  }

});

//
// Reducers

export const reducers = createHandleActions({

  [CLEAR_MUSIC_IMPORT]: () => {
    return { ...defaultState };
  },

  [SET_MUSIC_IMPORT_FOLDER]: (state, { payload }) => {
    return Object.assign({}, state, { importFolder: payload.folder });
  },

  // Patch a single item in the items array by id
  [UPDATE_MUSIC_IMPORT_ITEM]: (state, { payload }) => {
    const { id, ...changes } = payload;
    const items = state.items.map((item) => {
      if (item.id !== id) {
        return item;
      }

      return Object.assign({}, item, changes);
    });

    return Object.assign({}, state, { items });
  }

}, defaultState, section);
