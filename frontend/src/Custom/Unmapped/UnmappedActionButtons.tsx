import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { LocalWatchBufferItem } from './UnmappedTable';

interface UnmappedActionButtonsProps {
  item: LocalWatchBufferItem;
  onManualImport: (path: string) => void;
  onChanged: () => void;
}

function UnmappedActionButtons({
  item,
  onManualImport,
  onChanged,
}: UnmappedActionButtonsProps) {
  const retryMutation = useApiMutation<LocalWatchBufferItem, void>({
    path: `/localwatchbuffer/${item.id}/retry`,
    method: 'POST',
    mutationOptions: {
      onSuccess: onChanged,
    },
  });

  const ignoreMutation = useApiMutation<LocalWatchBufferItem, void>({
    path: `/localwatchbuffer/${item.id}/ignore`,
    method: 'POST',
    mutationOptions: {
      onSuccess: onChanged,
    },
  });

  const onManualImportPress = useCallback(() => {
    onManualImport(item.path);
  }, [item.path, onManualImport]);

  const onRetryPress = useCallback(() => {
    retryMutation.mutate();
  }, [retryMutation]);

  const onIgnorePress = useCallback(() => {
    ignoreMutation.mutate();
  }, [ignoreMutation]);

  const isBusy = retryMutation.isPending || ignoreMutation.isPending;

  return (
    <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
      <Button isDisabled={isBusy} onPress={onManualImportPress}>
        {translate('ManualImport')}
      </Button>
      <Button kind={kinds.PRIMARY} isDisabled={isBusy} onPress={onRetryPress}>
        Retry
      </Button>
      <Button kind={kinds.DEFAULT} isDisabled={isBusy} onPress={onIgnorePress}>
        Ignore
      </Button>
    </div>
  );
}

export default UnmappedActionButtons;
