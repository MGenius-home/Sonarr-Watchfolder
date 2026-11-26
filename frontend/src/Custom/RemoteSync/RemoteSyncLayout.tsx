import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback } from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import RemoteSyncTable from './RemoteSyncTable';

function RemoteSyncLayout() {
  const queryClient = useQueryClient();

  const syncMutation = useApiMutation<{ queued: boolean }, void>({
    path: '/remotesync/sync',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/remotesync'] });
      },
    },
  });

  const onSyncPress = useCallback(() => {
    syncMutation.mutate();
  }, [syncMutation]);

  const onRefreshPress = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/remotesync'] });
  }, [queryClient]);

  return (
    <PageContent title={translate('Remote Sync History')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label="Sync Now"
            iconName={icons.REFRESH}
            isSpinning={syncMutation.isPending}
            onPress={onSyncPress}
          />
          <PageToolbarButton
            label={translate('Refresh')}
            iconName={icons.UPDATE}
            onPress={onRefreshPress}
          />
        </PageToolbarSection>
      </PageToolbar>

      <PageContentBody>
        <RemoteSyncTable />
      </PageContentBody>
    </PageContent>
  );
}

export default RemoteSyncLayout;
