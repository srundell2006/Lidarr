import { createAction } from 'redux-actions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler, { createCancelSaveProviderHandler } from 'Store/Actions/Creators/createSaveProviderHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import monitorNewItemsOptions from 'Utilities/Artist/monitorNewItemsOptions';
import monitorOptions from 'Utilities/Artist/monitorOptions';

//
// Variables

export const section = 'settings.importFolders';

//
// Actions Types

export const FETCH_IMPORT_FOLDERS = 'settings/importFolders/fetchImportFolders';
export const SET_IMPORT_FOLDER_VALUE = 'settings/importFolders/setImportFolderValue';
export const SAVE_IMPORT_FOLDER = 'settings/importFolders/saveImportFolder';
export const CANCEL_SAVE_IMPORT_FOLDER = 'settings/importFolders/cancelSaveImportFolder';
export const DELETE_IMPORT_FOLDER = 'settings/importFolders/deleteImportFolder';

//
// Action Creators

export const fetchImportFolders = createThunk(FETCH_IMPORT_FOLDERS);
export const saveImportFolder = createThunk(SAVE_IMPORT_FOLDER);
export const cancelSaveImportFolder = createThunk(CANCEL_SAVE_IMPORT_FOLDER);
export const deleteImportFolder = createThunk(DELETE_IMPORT_FOLDER);

export const setImportFolderValue = createAction(SET_IMPORT_FOLDER_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

//
// Details

export default {
  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    schema: {
      defaultQualityProfileId: 0,
      defaultMetadataProfileId: 0,
      defaultMonitorOption: monitorOptions[0].key,
      defaultNewItemMonitorOption: monitorNewItemsOptions[0].key,
      defaultTags: [],
      automaticallyImport: false,
      folderType: 1
    },
    isSaving: false,
    saveError: null,
    items: [],
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {

    [FETCH_IMPORT_FOLDERS]: createFetchHandler(section, '/rootFolder'),

    [SAVE_IMPORT_FOLDER]: createSaveProviderHandler(section, '/rootFolder'),
    [CANCEL_SAVE_IMPORT_FOLDER]: createCancelSaveProviderHandler(section),
    [DELETE_IMPORT_FOLDER]: createRemoveItemHandler(section, '/rootFolder')

  },

  //
  // Reducers

  reducers: {
    [SET_IMPORT_FOLDER_VALUE]: createSetSettingValueReducer(section)
  }
};
