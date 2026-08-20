import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import RemoteSyncTable from './RemoteSyncTable';

function RemoteSyncLayout() {
  return (
    <PageContent title={translate('Remote Sync History')}>
      <PageContentBody>
        <RemoteSyncTable />
      </PageContentBody>
    </PageContent>
  );
}

export default RemoteSyncLayout;
