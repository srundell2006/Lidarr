import React from 'react';
import Modal from 'Components/Modal/Modal';
import RebuildArtistModalContent from './RebuildArtistModalContent';

interface RebuildArtistModalProps {
  isOpen: boolean;
  artistIds: number[];
  onModalClose: () => void;
}

function RebuildArtistModal(props: RebuildArtistModalProps) {
  const { isOpen, onModalClose, ...otherProps } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <RebuildArtistModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default RebuildArtistModal;
