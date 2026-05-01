import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { saveImportFolder, setImportFolderValue } from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import EditImportFolderModalContent from './EditImportFolderModalContent';

function createMapStateToProps() {
  return createSelector(
    (state, { id }) => id,
    (state) => state.settings.advancedSettings,
    (state) => state.settings.metadataProfiles,
    (state) => state.settings.importFolders,
    createProviderSettingsSelector('importFolders'),
    (id, advancedSettings, metadataProfiles, importFolders, importFolderSettings) => {
      return {
        advancedSettings,
        showMetadataProfile: metadataProfiles.items.length > 1,
        ...importFolderSettings,
        isFetching: importFolders.isFetching
      };
    }
  );
}

const mapDispatchToProps = {
  setImportFolderValue,
  saveImportFolder
};

class EditImportFolderModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidUpdate(prevProps, prevState) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalClose();
    }
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.setImportFolderValue({ name, value });
  };

  onSavePress = () => {
    this.props.setImportFolderValue({ name: 'folderType', value: 1 });
    this.props.saveImportFolder({ id: this.props.id });
  };

  //
  // Render

  render() {
    return (
      <EditImportFolderModalContent
        {...this.props}
        onSavePress={this.onSavePress}
        onInputChange={this.onInputChange}
      />
    );
  }
}

EditImportFolderModalContentConnector.propTypes = {
  id: PropTypes.number,
  isFetching: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  item: PropTypes.object.isRequired,
  setImportFolderValue: PropTypes.func.isRequired,
  saveImportFolder: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(EditImportFolderModalContentConnector);
