import { orderBy } from 'lodash';
import React, { useCallback, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import Artist from 'Artist/Artist';
import { REBUILD_ARTIST } from 'Commands/commandNames';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import { executeCommand } from 'Store/Actions/commandActions';
import createAllArtistSelector from 'Store/Selectors/createAllArtistSelector';

interface RebuildArtistModalContentProps {
  artistIds: number[];
  onModalClose: () => void;
}

function RebuildArtistModalContent(props: RebuildArtistModalContentProps) {
  const { artistIds, onModalClose } = props;

  const allArtists: Artist[] = useSelector(createAllArtistSelector());
  const dispatch = useDispatch();

  const artistNames = useMemo(() => {
    const artists = artistIds.reduce((acc: Artist[], id) => {
      const a = allArtists.find((a) => a.id === id);

      if (a) {
        acc.push(a);
      }

      return acc;
    }, []);

    return orderBy(artists, ['sortName']).map((a) => a.artistName);
  }, [artistIds, allArtists]);

  const onRebuildPress = useCallback(() => {
    dispatch(
      executeCommand({
        name: REBUILD_ARTIST,
        artistIds,
      })
    );

    onModalClose();
  }, [artistIds, onModalClose, dispatch]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>Rebuild Database for Selected Artists</ModalHeader>

      <ModalBody>
        <div>
          Are you sure you want to rebuild the database for the{' '}
          {artistNames.length} selected{' '}
          {artistNames.length === 1 ? 'artist' : 'artists'}? This will rescan
          their folders and rename all files to match the current naming
          convention.
        </div>

        <ul>
          {artistNames.map((artistName) => {
            return <li key={artistName}>{artistName}</li>;
          })}
        </ul>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>Cancel</Button>

        <Button kind={kinds.WARNING} onPress={onRebuildPress}>
          Rebuild Database
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default RebuildArtistModalContent;
