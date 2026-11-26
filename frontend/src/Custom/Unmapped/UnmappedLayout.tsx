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
import UnmappedTable from './UnmappedTable';

function UnmappedLayout() {
  const queryClient = useQueryClient();

  const scanMutation = useApiMutation<{ queued: boolean }, void>({
    path: '/localwatchbuffer/scan',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/localwatchbuffer'] });
      },
    },
  });

  const onScanPress = useCallback(() => {
    scanMutation.mutate();
  }, [scanMutation]);

  const onRefreshPress = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/localwatchbuffer'] });
  }, [queryClient]);

  return (
    <PageContent title={translate('Unmapped Local Files')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label="Scan Now"
            iconName={icons.REFRESH}
            isSpinning={scanMutation.isPending}
            onPress={onScanPress}
          />
          <PageToolbarButton
            label={translate('Refresh')}
            iconName={icons.UPDATE}
            onPress={onRefreshPress}
          />
        </PageToolbarSection>
      </PageToolbar>

      <PageContentBody>
        <UnmappedTable />
      </PageContentBody>
    </PageContent>
  );
}

export default UnmappedLayout;
