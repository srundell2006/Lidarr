import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { deleteImportFolder, fetchImportFolders } from 'Store/Actions/settingsActions';
import createImportFoldersSelector from 'Store/Selectors/createImportFoldersSelector';
import ImportFolders from './ImportFolders';

function createMapStateToProps() {
  return createSelector(
    createImportFoldersSelector(),
    (state) => state.settings.qualityProfiles,
    (state) => state.settings.metadataProfiles,
    (importFolders, quality, metadata) => {
      return {
        qualityProfiles: quality.items,
        metadataProfiles: metadata.items,
        ...importFolders,
        items: importFolders.items.filter((f) => f.folderType === 1)
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchImportFolders: fetchImportFolders,
  dispatchDeleteImportFolder: deleteImportFolder
};

class ImportFoldersConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    this.props.dispatchFetchImportFolders();
  }

  //
  // Listeners

  onConfirmDeleteImportFolder = (id) => {
    this.props.dispatchDeleteImportFolder({ id });
  };

  //
  // Render

  render() {
    return (
      <ImportFolders
        {...this.props}
        onConfirmDeleteImportFolder={this.onConfirmDeleteImportFolder}
      />
    );
  }
}

ImportFoldersConnector.propTypes = {
  dispatchFetchImportFolders: PropTypes.func.isRequired,
  dispatchDeleteImportFolder: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(ImportFoldersConnector);
