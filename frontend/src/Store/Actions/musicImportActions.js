import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import createHandleActions from './Creators/createHandleActions';
import { set, update } from './baseActions';

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
export const CLEAR_MUSIC_IMPORT = 'musicImport/clearMusicImport';
export const SET_MUSIC_IMPORT_FOLDER = 'musicImport/setMusicImportFolder';

//
// Action Creators

export const fetchMusicImportItems = createThunk(FETCH_MUSIC_IMPORT_ITEMS);
export const saveMusicImportItems = createThunk(SAVE_MUSIC_IMPORT_ITEMS);
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

  [SAVE_MUSIC_IMPORT_ITEMS]: function(getState, payload, dispatch) {
    const { ids } = payload;
    const items = getState()[section].items;

    dispatch(set({ section, isSaving: true, saveError: null }));

    const requestPayload = ids.map((id) => {
      const item = items.find((i) => i.id === id);

      return {
        id,
        path: item.path,
        artistId: item.artist ? item.artist.id : undefined,
        albumId: item.album ? item.album.id : undefined,
        albumReleaseId: item.albumReleaseId || undefined,
        trackIds: (item.tracks || []).map((t) => t.id),
        quality: item.quality,
        downloadId: item.downloadId || undefined,
        additionalFile: false,
        replaceExistingFiles: false,
        disableReleaseSwitching: false
      };
    });

    const { request } = createAjaxRequest({
      method: 'POST',
      url: '/manualimport',
      contentType: 'application/json',
      data: JSON.stringify(requestPayload)
    });

    request.done(() => {
      dispatch(set({ section, isSaving: false, saveError: null }));
      // Re-fetch to show updated state
      const importFolder = getState()[section].importFolder;
      if (importFolder) {
        dispatch(fetchMusicImportItems({ folder: importFolder }));
      }
    });

    request.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
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
  }

}, defaultState, section);
