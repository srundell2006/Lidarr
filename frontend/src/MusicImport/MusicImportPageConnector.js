import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import {
  clearMusicImport,
  deleteMusicImportDuplicates,
  fetchMusicImportItems,
  lookupMusicImportRecording,
  saveMusicImportItems
} from 'Store/Actions/musicImportActions';
import { fetchImportFolders } from 'Store/Actions/settingsActions';
import MusicImportPage from './MusicImportPage';

function createMapStateToProps() {
  return createSelector(
    (state) => state.musicImport,
    (state) => state.settings.importFolders,
    (musicImport, importFolders) => {
      const importFolder = importFolders.items.find((f) => f.folderType === 1) || null;

      return {
        importFolder,
        isFetching: musicImport.isFetching,
        isPopulated: musicImport.isPopulated,
        isSaving: musicImport.isSaving,
        saveError: musicImport.saveError,
        error: musicImport.error,
        items: musicImport.items
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchMusicImportItems: fetchMusicImportItems,
  dispatchSaveMusicImportItems: saveMusicImportItems,
  dispatchDeleteMusicImportDuplicates: deleteMusicImportDuplicates,
  dispatchLookupMusicImportRecording: lookupMusicImportRecording,
  dispatchClearMusicImport: clearMusicImport,
  dispatchFetchImportFolders: fetchImportFolders
};

class MusicImportPageConnector extends Component {

  componentDidMount() {
    this.props.dispatchFetchImportFolders();
  }

  componentDidUpdate(prevProps) {
    const { importFolder } = this.props;

    if (importFolder && (!prevProps.importFolder || prevProps.importFolder.path !== importFolder.path)) {
      this.props.dispatchFetchMusicImportItems({ folder: importFolder.path });
    }
  }

  componentWillUnmount() {
    this.props.dispatchClearMusicImport();
  }

  onRefreshPress = () => {
    const { importFolder } = this.props;
    if (importFolder) {
      this.props.dispatchFetchMusicImportItems({ folder: importFolder.path });
    }
  };

  onImportPress = (ids) => {
    this.props.dispatchSaveMusicImportItems({ ids });
  };

  onDeleteDuplicatesPress = (ids) => {
    this.props.dispatchDeleteMusicImportDuplicates({ ids });
  };

  onLookupRecording = (itemId, recordingId) => {
    this.props.dispatchLookupMusicImportRecording({ itemId, recordingId });
  };

  render() {
    return (
      <MusicImportPage
        {...this.props}
        onRefreshPress={this.onRefreshPress}
        onImportPress={this.onImportPress}
        onDeleteDuplicatesPress={this.onDeleteDuplicatesPress}
        onLookupRecording={this.onLookupRecording}
      />
    );
  }
}

MusicImportPageConnector.propTypes = {
  importFolder: PropTypes.object,
  dispatchFetchMusicImportItems: PropTypes.func.isRequired,
  dispatchSaveMusicImportItems: PropTypes.func.isRequired,
  dispatchDeleteMusicImportDuplicates: PropTypes.func.isRequired,
  dispatchLookupMusicImportRecording: PropTypes.func.isRequired,
  dispatchClearMusicImport: PropTypes.func.isRequired,
  dispatchFetchImportFolders: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(MusicImportPageConnector);
