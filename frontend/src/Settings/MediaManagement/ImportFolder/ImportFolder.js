import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import EditImportFolderModalConnector from './EditImportFolderModalConnector';
import styles from './ImportFolder.css';

class ImportFolder extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isEditImportFolderModalOpen: false,
      isDeleteImportFolderModalOpen: false
    };
  }

  //
  // Listeners

  onEditImportFolderPress = () => {
    this.setState({ isEditImportFolderModalOpen: true });
  };

  onEditImportFolderModalClose = () => {
    this.setState({ isEditImportFolderModalOpen: false });
  };

  onDeleteImportFolderPress = () => {
    this.setState({
      isEditImportFolderModalOpen: false,
      isDeleteImportFolderModalOpen: true
    });
  };

  onDeleteImportFolderModalClose = () => {
    this.setState({ isDeleteImportFolderModalOpen: false });
  };

  onConfirmDeleteImportFolder = () => {
    this.props.onConfirmDeleteImportFolder(this.props.id);
  };

  //
  // Render

  render() {
    const {
      id,
      name,
      path,
      qualityProfile,
      metadataProfile
    } = this.props;

    return (
      <Card
        className={styles.importFolder}
        overlayContent={true}
        onPress={this.onEditImportFolderPress}
      >
        <div className={styles.name}>
          {name}
        </div>

        <div className={styles.enabled}>
          <Label kind={kinds.SUCCESS}>
            {path}
          </Label>

          <Label kind={qualityProfile?.name ? kinds.SUCCESS : kinds.DANGER}>
            {qualityProfile?.name || translate('None')}
          </Label>

          <Label kind={metadataProfile?.name ? kinds.SUCCESS : kinds.DANGER}>
            {metadataProfile?.name || translate('None')}
          </Label>
        </div>

        <EditImportFolderModalConnector
          id={id}
          isOpen={this.state.isEditImportFolderModalOpen}
          onModalClose={this.onEditImportFolderModalClose}
          onDeleteImportFolderPress={this.onDeleteImportFolderPress}
        />

        <ConfirmModal
          isOpen={this.state.isDeleteImportFolderModalOpen}
          kind={kinds.DANGER}
          title={translate('RemoveRootFolder')}
          message={translate('RemoveRootFolderArtistsMessageText', { name })}
          confirmLabel={translate('Remove')}
          onConfirm={this.onConfirmDeleteImportFolder}
          onCancel={this.onDeleteImportFolderModalClose}
        />
      </Card>
    );
  }
}

ImportFolder.propTypes = {
  id: PropTypes.number.isRequired,
  name: PropTypes.string.isRequired,
  path: PropTypes.string.isRequired,
  qualityProfile: PropTypes.object.isRequired,
  metadataProfile: PropTypes.object.isRequired,
  onConfirmDeleteImportFolder: PropTypes.func.isRequired
};

export default ImportFolder;
