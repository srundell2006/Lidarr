import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { cancelSaveImportFolder } from 'Store/Actions/settingsActions';
import EditImportFolderModal from './EditImportFolderModal';

function createMapDispatchToProps(dispatch, props) {
  const section = 'settings.importFolders';

  return {
    dispatchClearPendingChanges() {
      dispatch(clearPendingChanges({ section }));
    },

    dispatchCancelSaveImportFolder() {
      dispatch(cancelSaveImportFolder({ section }));
    }
  };
}

class EditImportFolderModalConnector extends Component {

  //
  // Listeners

  onModalClose = () => {
    this.props.dispatchClearPendingChanges();
    this.props.dispatchCancelSaveImportFolder();
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    const {
      dispatchClearPendingChanges,
      dispatchCancelSaveImportFolder,
      ...otherProps
    } = this.props;

    return (
      <EditImportFolderModal
        {...otherProps}
        onModalClose={this.onModalClose}
      />
    );
  }
}

EditImportFolderModalConnector.propTypes = {
  onModalClose: PropTypes.func.isRequired,
  dispatchClearPendingChanges: PropTypes.func.isRequired,
  dispatchCancelSaveImportFolder: PropTypes.func.isRequired
};

export default connect(null, createMapDispatchToProps)(EditImportFolderModalConnector);
