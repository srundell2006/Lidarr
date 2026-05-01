import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import EditImportFolderModalConnector from './EditImportFolderModalConnector';
import ImportFolder from './ImportFolder';
import styles from './ImportFolders.css';

class ImportFolders extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isAddImportFolderModalOpen: false
    };
  }

  //
  // Listeners

  onAddImportFolderPress = () => {
    this.setState({ isAddImportFolderModalOpen: true });
  };

  onAddImportFolderModalClose = () => {
    this.setState({ isAddImportFolderModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      items,
      qualityProfiles,
      metadataProfiles,
      onConfirmDeleteImportFolder,
      ...otherProps
    } = this.props;

    return (
      <FieldSet legend={translate('ImportFolders')}>
        <PageSectionContent
          errorMessage={translate('UnableToLoadRootFolders')}
          {...otherProps}
        >
          <div className={styles.importFolders}>
            {
              items.map((item) => {
                const qualityProfile = qualityProfiles.find((profile) => profile.id === item.defaultQualityProfileId);
                const metadataProfile = metadataProfiles.find((profile) => profile.id === item.defaultMetadataProfileId);
                return (
                  <ImportFolder
                    key={item.id}
                    {...item}
                    qualityProfile={qualityProfile}
                    metadataProfile={metadataProfile}
                    onConfirmDeleteImportFolder={onConfirmDeleteImportFolder}
                  />
                );
              })
            }

            <Card
              className={styles.addImportFolder}
              onPress={this.onAddImportFolderPress}
            >
              <div className={styles.center}>
                <Icon
                  name={icons.ADD}
                  size={45}
                />
              </div>
            </Card>
          </div>

          <EditImportFolderModalConnector
            isOpen={this.state.isAddImportFolderModalOpen}
            onModalClose={this.onAddImportFolderModalClose}
          />
        </PageSectionContent>
      </FieldSet>
    );
  }
}

ImportFolders.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  qualityProfiles: PropTypes.arrayOf(PropTypes.object).isRequired,
  metadataProfiles: PropTypes.arrayOf(PropTypes.object).isRequired,
  onConfirmDeleteImportFolder: PropTypes.func.isRequired
};

export default ImportFolders;
